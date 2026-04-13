using BraillePrinter.Models;

namespace BraillePrinter.Managers
{
    /// <summary>
    /// 한글 텍스트를 점자 셀로 변환하고 물리 좌표를 계산하는 싱글톤 매니저.
    ///
    /// 변환 기준: 2020년 문화체육관광부 고시 '한국 점자 규정' (풀어쓰기 방식)
    ///
    /// 비트 인코딩: bit0=점1, bit1=점2, bit2=점3, bit3=점4, bit4=점5, bit5=점6
    ///
    /// CurrentDotCoordinates는 하드웨어(G-code 생성 등)에서 직접 사용 가능합니다.
    /// </summary>
    public sealed class BrailleManager
    {
        // ── 싱글톤 ───────────────────────────────────────────────────────
        public static readonly BrailleManager Instance = new();

        // ── 공개 상태 ─────────────────────────────────────────────────────

        /// <summary>마지막으로 변환된 점자 셀 목록</summary>
        public IReadOnlyList<BrailleCell> CurrentCells { get; private set; }
            = Array.Empty<BrailleCell>();

        /// <summary>
        /// 마지막으로 계산된 물리 좌표 목록 (mm 단위, 용지 좌상단 기준).
        /// 하드웨어 제어 시 이 목록을 직접 사용하세요.
        /// </summary>
        public IReadOnlyList<DotCoordinate> CurrentDotCoordinates { get; private set; }
            = Array.Empty<DotCoordinate>();

        /// <summary>변환이 완료되면 발생합니다.</summary>
        public event Action? BrailleUpdated;

        private BrailleManager() { }

        // ── 공개 API ─────────────────────────────────────────────────────

        /// <summary>텍스트를 점자로 변환하고 CurrentCells/CurrentDotCoordinates를 갱신합니다.</summary>
        public void Convert(string text)
        {
            var cells = BuildCells(text);
            CurrentCells           = cells.AsReadOnly();
            CurrentDotCoordinates  = CalculateCoordinates(cells).AsReadOnly();
            BrailleUpdated?.Invoke();
        }

        // ── 한국 점자 코드 테이블 (2020 한국 점자 규정) ───────────────────
        #region Braille Code Tables

        /*
         * 초성 인덱스 순서 (Unicode 기준 19개):
         * 0:ㄱ 1:ㄲ 2:ㄴ 3:ㄷ 4:ㄸ 5:ㄹ 6:ㅁ 7:ㅂ 8:ㅃ 9:ㅅ
         * 10:ㅆ 11:ㅇ 12:ㅈ 13:ㅉ 14:ㅊ 15:ㅋ 16:ㅌ 17:ㅍ 18:ㅎ
         */
        private static readonly byte[] InitialConsonant = new byte[19]
        {
            0x08, // ㄱ  점4
            0x28, // ㄲ  점4,6
            0x09, // ㄴ  점1,4
            0x0A, // ㄷ  점2,4
            0x2A, // ㄸ  점2,4,6
            0x1B, // ㄹ  점1,2,4,5
            0x19, // ㅁ  점1,4,5
            0x0B, // ㅂ  점1,2,4
            0x2B, // ㅃ  점1,2,4,6
            0x0D, // ㅅ  점1,3,4
            0x2D, // ㅆ  점1,3,4,6
            0x00, // ㅇ  초성 ㅇ 생략
            0x1A, // ㅈ  점2,4,5
            0x3A, // ㅉ  점2,4,5,6
            0x39, // ㅊ  점1,4,5,6
            0x0F, // ㅋ  점1,2,3,4
            0x1E, // ㅌ  점2,3,4,5
            0x1D, // ㅍ  점1,3,4,5
            0x33, // ㅎ  점1,2,5,6
        };

        /*
         * 중성 인덱스 순서 (Unicode 기준 21개):
         * 0:ㅏ 1:ㅐ 2:ㅑ 3:ㅒ 4:ㅓ 5:ㅔ 6:ㅕ 7:ㅖ 8:ㅗ 9:ㅘ
         * 10:ㅙ 11:ㅚ 12:ㅛ 13:ㅜ 14:ㅝ 15:ㅞ 16:ㅟ 17:ㅠ 18:ㅡ 19:ㅢ 20:ㅣ
         *
         * 합성 모음(0xFF)은 CompoundVowel 테이블에서 두 셀로 분리합니다.
         */
        private static readonly byte[] Vowel = new byte[21]
        {
            0x13, // ㅏ  점1,2,5
            0x1C, // ㅐ  점3,4,5
            0x1B, // ㅑ  점1,2,4,5
            0x3C, // ㅒ  점3,4,5,6
            0x16, // ㅓ  점2,3,5
            0x32, // ㅔ  점2,5,6
            0x31, // ㅕ  점1,5,6
            0x33, // ㅖ  점1,2,5,6
            0x15, // ㅗ  점1,3,5
            0xFF, // ㅘ  → 두 셀
            0xFF, // ㅙ  → 두 셀
            0xFF, // ㅚ  → 두 셀
            0x17, // ㅛ  점1,2,3,5
            0x36, // ㅜ  점2,3,5,6
            0xFF, // ㅝ  → 두 셀
            0xFF, // ㅞ  → 두 셀
            0xFF, // ㅟ  → 두 셀
            0x35, // ㅠ  점1,3,5,6
            0x34, // ㅡ  점3,5,6
            0xFF, // ㅢ  → 두 셀
            0x25, // ㅣ  점1,3,6
        };

        // 합성 모음 분해: 중성 인덱스 → (앞 셀 코드, 뒤 셀 코드)
        private static readonly Dictionary<int, (byte A, byte B)> CompoundVowel = new()
        {
            {  9, (0x15, 0x13) }, // ㅘ = ㅗ + ㅏ
            { 10, (0x15, 0x1C) }, // ㅙ = ㅗ + ㅐ
            { 11, (0x15, 0x25) }, // ㅚ = ㅗ + ㅣ
            { 14, (0x36, 0x16) }, // ㅝ = ㅜ + ㅓ
            { 15, (0x36, 0x32) }, // ㅞ = ㅜ + ㅔ
            { 16, (0x36, 0x25) }, // ㅟ = ㅜ + ㅣ
            { 19, (0x34, 0x25) }, // ㅢ = ㅡ + ㅣ
        };

        /*
         * 종성 인덱스 순서 (Unicode 기준, 0=없음, 28개):
         * 0:없음 1:ㄱ 2:ㄲ 3:ㄳ 4:ㄴ 5:ㄵ 6:ㄶ 7:ㄷ 8:ㄹ 9:ㄺ
         * 10:ㄻ 11:ㄼ 12:ㄽ 13:ㄾ 14:ㄿ 15:ㅀ 16:ㅁ 17:ㅂ 18:ㅄ
         * 19:ㅅ 20:ㅆ 21:ㅇ 22:ㅈ 23:ㅊ 24:ㅋ 25:ㅌ 26:ㅍ 27:ㅎ
         *
         * 겹받침(0xFF)은 CompoundFinal 테이블에서 두 셀로 분리합니다.
         */
        private static readonly byte[] FinalConsonant = new byte[28]
        {
            0x00, // 0  없음
            0x08, // 1  ㄱ  점4
            0x28, // 2  ㄲ  점4,6
            0xFF, // 3  ㄳ  → 두 셀
            0x09, // 4  ㄴ  점1,4
            0xFF, // 5  ㄵ  → 두 셀
            0xFF, // 6  ㄶ  → 두 셀
            0x0A, // 7  ㄷ  점2,4
            0x1B, // 8  ㄹ  점1,2,4,5
            0xFF, // 9  ㄺ  → 두 셀
            0xFF, // 10 ㄻ  → 두 셀
            0xFF, // 11 ㄼ  → 두 셀
            0xFF, // 12 ㄽ  → 두 셀
            0xFF, // 13 ㄾ  → 두 셀
            0xFF, // 14 ㄿ  → 두 셀
            0xFF, // 15 ㅀ  → 두 셀
            0x19, // 16 ㅁ  점1,4,5
            0x0B, // 17 ㅂ  점1,2,4
            0xFF, // 18 ㅄ  → 두 셀
            0x0D, // 19 ㅅ  점1,3,4
            0x2D, // 20 ㅆ  점1,3,4,6
            0x24, // 21 ㅇ  점3,6  (받침 전용)
            0x1A, // 22 ㅈ  점2,4,5
            0x39, // 23 ㅊ  점1,4,5,6
            0x0F, // 24 ㅋ  점1,2,3,4
            0x1E, // 25 ㅌ  점2,3,4,5
            0x1D, // 26 ㅍ  점1,3,4,5
            0x33, // 27 ㅎ  점1,2,5,6
        };

        // 겹받침 분해: 종성 인덱스 → (앞 셀 코드, 뒤 셀 코드)
        private static readonly Dictionary<int, (byte A, byte B)> CompoundFinal = new()
        {
            {  3, (0x08, 0x0D) }, // ㄳ = ㄱ + ㅅ
            {  5, (0x09, 0x1A) }, // ㄵ = ㄴ + ㅈ
            {  6, (0x09, 0x33) }, // ㄶ = ㄴ + ㅎ
            {  9, (0x1B, 0x08) }, // ㄺ = ㄹ + ㄱ
            { 10, (0x1B, 0x19) }, // ㄻ = ㄹ + ㅁ
            { 11, (0x1B, 0x0B) }, // ㄼ = ㄹ + ㅂ
            { 12, (0x1B, 0x0D) }, // ㄽ = ㄹ + ㅅ
            { 13, (0x1B, 0x1E) }, // ㄾ = ㄹ + ㅌ
            { 14, (0x1B, 0x1D) }, // ㄿ = ㄹ + ㅍ
            { 15, (0x1B, 0x33) }, // ㅀ = ㄹ + ㅎ
            { 18, (0x0B, 0x0D) }, // ㅄ = ㅂ + ㅅ
        };

        // ── 영문·숫자 코드 (Grade 1 Braille / 영문 점자) ─────────────────
        #region Latin / Number Tables

        /*
         * 영문 소문자 a~z (표준 6점 점자 Grade 1)
         * index 0=a, 1=b, ..., 25=z
         */
        private static readonly byte[] LatinLower = new byte[26]
        {
            0x01, // a: 점1
            0x03, // b: 점1,2
            0x09, // c: 점1,4
            0x19, // d: 점1,4,5
            0x11, // e: 점1,5
            0x0B, // f: 점1,2,4
            0x1B, // g: 점1,2,4,5
            0x13, // h: 점1,2,5
            0x0A, // i: 점2,4
            0x1A, // j: 점2,4,5
            0x05, // k: 점1,3
            0x07, // l: 점1,2,3
            0x0D, // m: 점1,3,4
            0x1D, // n: 점1,3,4,5
            0x15, // o: 점1,3,5
            0x0F, // p: 점1,2,3,4
            0x1F, // q: 점1,2,3,4,5
            0x17, // r: 점1,2,3,5
            0x0E, // s: 점2,3,4
            0x1E, // t: 점2,3,4,5
            0x25, // u: 점1,3,6
            0x27, // v: 점1,2,3,6
            0x3A, // w: 점2,4,5,6
            0x2D, // x: 점1,3,4,6
            0x3D, // y: 점1,3,4,5,6
            0x35, // z: 점1,3,5,6
        };

        /*
         * 숫자 0~9 (숫자 표시자 뒤에 따르는 패턴)
         * 숫자 1~9 → 소문자 a~i, 0 → j 패턴 사용
         */
        private static readonly byte[] DigitCodes = new byte[10]
        {
            0x1A, // 0 → j 패턴: 점2,4,5
            0x01, // 1 → a 패턴: 점1
            0x03, // 2 → b 패턴: 점1,2
            0x09, // 3 → c 패턴: 점1,4
            0x19, // 4 → d 패턴: 점1,4,5
            0x11, // 5 → e 패턴: 점1,5
            0x0B, // 6 → f 패턴: 점1,2,4
            0x1B, // 7 → g 패턴: 점1,2,4,5
            0x13, // 8 → h 패턴: 점1,2,5
            0x0A, // 9 → i 패턴: 점2,4
        };

        // 기본 구두점
        private static readonly Dictionary<char, byte> PunctuationCodes = new()
        {
            { '.', 0x04 }, // 마침표:  점3
            { ',', 0x02 }, // 쉼표:    점2
            { '?', 0x26 }, // 물음표:  점2,3,6
            { '!', 0x16 }, // 느낌표:  점2,3,5
            { '-', 0x24 }, // 하이픈:  점3,6
            { ':', 0x12 }, // 콜론:    점2,5
            { ';', 0x06 }, // 세미콜론: 점2,3
            { '(', 0x2E }, // 여는 괄호: 점2,3,4,6
            { ')', 0x2E }, // 닫는 괄호: 점2,3,4,6
        };

        #endregion

        #endregion

        // ── 변환 로직 ─────────────────────────────────────────────────────

        private List<BrailleCell> BuildCells(string text)
        {
            // 1단계: 텍스트 → 점자 패턴 플랫 리스트
            var patterns = new List<byte>();

            foreach (char ch in text)
            {
                if (ch == ' ' || ch == '\u00A0')
                {
                    patterns.Add(0x00); // 공백 셀
                    continue;
                }

                if (ch == '\r' || ch == '\n')
                {
                    patterns.Add(0xFE); // 줄바꿈 마커 (레이아웃 단계에서 처리)
                    continue;
                }

                if (ch >= 0xAC00 && ch <= 0xD7A3)
                {
                    AppendKoreanSyllable(ch, patterns);
                    continue;
                }

                // 영문 소문자
                if (ch >= 'a' && ch <= 'z')
                {
                    patterns.Add(LatinLower[ch - 'a']);
                    continue;
                }

                // 영문 대문자 → 대문자 표시자(점6) + 소문자 코드
                if (ch >= 'A' && ch <= 'Z')
                {
                    patterns.Add(0x20);                     // 대문자 표시자: 점6
                    patterns.Add(LatinLower[ch - 'A']);
                    continue;
                }

                // 숫자 → 숫자 표시자(점3,4,5,6) + 숫자 코드
                if (ch >= '0' && ch <= '9')
                {
                    patterns.Add(0x3C);                     // 숫자 표시자: 점3,4,5,6
                    patterns.Add(DigitCodes[ch - '0']);
                    continue;
                }

                // 기본 구두점
                if (PunctuationCodes.TryGetValue(ch, out byte punctCode))
                {
                    patterns.Add(punctCode);
                    continue;
                }

                // 그 외 문자: 생략
            }

            // 2단계: 패턴 리스트 → 셀 레이아웃 (줄바꿈 포함)
            return LayoutCells(patterns);
        }

        private static void AppendKoreanSyllable(char syllable, List<byte> patterns)
        {
            int code     = syllable - 0xAC00;
            int initIdx  = code / (21 * 28);
            int vowelIdx = code % (21 * 28) / 28;
            int finalIdx = code % 28;

            // 초성 (ㅇ 초성은 코드 0x00 → 생략)
            byte initCode = InitialConsonant[initIdx];
            if (initCode != 0x00)
                patterns.Add(initCode);

            // 중성 (합성 모음은 두 셀)
            if (Vowel[vowelIdx] == 0xFF)
            {
                var (a, b) = CompoundVowel[vowelIdx];
                patterns.Add(a);
                patterns.Add(b);
            }
            else
            {
                patterns.Add(Vowel[vowelIdx]);
            }

            // 종성 (없으면 0, 겹받침은 두 셀)
            if (finalIdx > 0)
            {
                if (FinalConsonant[finalIdx] == 0xFF)
                {
                    var (a, b) = CompoundFinal[finalIdx];
                    patterns.Add(a);
                    patterns.Add(b);
                }
                else
                {
                    patterns.Add(FinalConsonant[finalIdx]);
                }
            }
        }

        private static List<BrailleCell> LayoutCells(List<byte> patterns)
        {
            var p            = ParameterManager.Instance.Parameters;
            int cellsPerLine = p.MaxCellsPerLine;
            int col = 0, row = 0;
            var cells = new List<BrailleCell>();

            foreach (byte pattern in patterns)
            {
                // 개행 마커
                if (pattern == 0xFE)
                {
                    row++;
                    col = 0;
                    continue;
                }

                // 줄 시작 공백 무시
                if (pattern == 0x00 && col == 0)
                    continue;

                // 자동 줄 바꿈
                if (col >= cellsPerLine)
                {
                    col = 0;
                    row++;
                }

                cells.Add(new BrailleCell
                {
                    DotPattern = pattern,
                    Column     = col,
                    Row        = row
                });

                if (pattern != 0x00)  // 공백 셀도 한 자리 차지
                    col++;
                else
                    col++;
            }

            return cells;
        }

        // ── 물리 좌표 계산 ────────────────────────────────────────────────

        private static List<DotCoordinate> CalculateCoordinates(List<BrailleCell> cells)
        {
            var p    = ParameterManager.Instance.Parameters;
            var dots = new List<DotCoordinate>();

            foreach (var cell in cells)
            {
                // 셀 기준점 (점1의 절대 좌표)
                double cellX = p.MarginLeft + cell.Column * p.CellSpacing;
                double cellY = p.MarginTop  + cell.Row    * p.LineSpacing;

                for (int dotNum = 1; dotNum <= 6; dotNum++)
                {
                    if (!cell.HasDot(dotNum)) continue;

                    // 점 번호 → 셀 내 오프셋
                    // 1,2,3 : 왼쪽 열  /  4,5,6 : 오른쪽 열
                    double offsetX = (dotNum <= 3) ? 0.0 : p.DotSpacing;
                    double offsetY = ((dotNum - 1) % 3) * p.DotSpacing;

                    dots.Add(new DotCoordinate(
                        x:          cellX + offsetX,
                        y:          cellY + offsetY,
                        cellColumn: cell.Column,
                        cellRow:    cell.Row,
                        dotNumber:  dotNum
                    ));
                }
            }

            return dots;
        }
    }
}
