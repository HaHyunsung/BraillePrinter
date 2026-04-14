namespace BraillePrinter.Converters
{
    /// <summary>
    /// 한국 점자 규정 코드 테이블을 직접 선언하여 변환하는 구현체.
    /// liblouis DLL 없이 동작하며, 기본(백업) 변환기로 항상 사용 가능합니다.
    ///
    /// 기준: 2020년 문화체육관광부 고시 '한국 점자 규정' (풀어쓰기 방식)
    /// </summary>
    public class ManualBrailleConverter : IBrailleConverter
    {
        public string Name        => "직접 테이블 (Manual)";
        public string Description => "내장 코드 테이블 기반. 항상 사용 가능. 약자 미지원.";
        public bool   IsAvailable => true;
        public string UnavailableReason => string.Empty;

        public List<byte> Convert(string text)
        {
            var patterns = new List<byte>();

            foreach (char ch in text)
            {
                if (ch == ' ' || ch == '\u00A0')
                {
                    patterns.Add(0x00);
                    continue;
                }

                if (ch == '\r') continue;
                if (ch == '\n')
                {
                    patterns.Add(0xFE);
                    continue;
                }

                if (ch >= 0xAC00 && ch <= 0xD7A3)
                {
                    AppendKoreanSyllable(ch, patterns);
                    continue;
                }

                if (ch >= 'a' && ch <= 'z')
                {
                    patterns.Add(LatinLower[ch - 'a']);
                    continue;
                }

                if (ch >= 'A' && ch <= 'Z')
                {
                    patterns.Add(0x20);                         // 대문자 표시자: 점6
                    patterns.Add(LatinLower[ch - 'A']);
                    continue;
                }

                if (ch >= '0' && ch <= '9')
                {
                    patterns.Add(0x3C);                         // 숫자 표시자: 점3,4,5,6
                    patterns.Add(DigitCodes[ch - '0']);
                    continue;
                }

                if (PunctuationCodes.TryGetValue(ch, out byte punctCode))
                {
                    patterns.Add(punctCode);
                    continue;
                }

                // 그 외 문자: 생략
            }

            return patterns;
        }

        // ── 한글 음절 분해 ────────────────────────────────────────────────

        private static void AppendKoreanSyllable(char syllable, List<byte> patterns)
        {
            int code     = syllable - 0xAC00;
            int initIdx  = code / (21 * 28);
            int vowelIdx = code % (21 * 28) / 28;
            int finalIdx = code % 28;

            // 초성 (ㅇ → 0x00 → 생략)
            byte initCode = InitialConsonant[initIdx];
            if (initCode != 0x00) patterns.Add(initCode);

            // 중성 (합성 모음 → 두 셀)
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

            // 종성 (없으면 finalIdx=0, 겹받침 → 두 셀)
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

        // ══════════════════════════════════════════════════════════════════
        // 한국 점자 코드 테이블 (2020 한국 점자 규정)
        // 비트 인코딩: bit0=점1, bit1=점2, bit2=점3, bit3=점4, bit4=점5, bit5=점6
        // ══════════════════════════════════════════════════════════════════
        #region Korean Braille Code Tables

        // 초성 (Unicode 기준 19개: ㄱ ㄲ ㄴ ㄷ ㄸ ㄹ ㅁ ㅂ ㅃ ㅅ ㅆ ㅇ ㅈ ㅉ ㅊ ㅋ ㅌ ㅍ ㅎ)
        private static readonly byte[] InitialConsonant = {
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
            0x00, // ㅇ  초성 생략
            0x1A, // ㅈ  점2,4,5
            0x3A, // ㅉ  점2,4,5,6
            0x39, // ㅊ  점1,4,5,6
            0x0F, // ㅋ  점1,2,3,4
            0x1E, // ㅌ  점2,3,4,5
            0x1D, // ㅍ  점1,3,4,5
            0x33, // ㅎ  점1,2,5,6
        };

        // 중성 (21개: ㅏ ㅐ ㅑ ㅒ ㅓ ㅔ ㅕ ㅖ ㅗ ㅘ ㅙ ㅚ ㅛ ㅜ ㅝ ㅞ ㅟ ㅠ ㅡ ㅢ ㅣ)
        // 0xFF = 합성 모음 → CompoundVowel 테이블에서 두 셀로 분리
        private static readonly byte[] Vowel = {
            0x13, // ㅏ  점1,2,5
            0x1C, // ㅐ  점3,4,5
            0x1B, // ㅑ  점1,2,4,5
            0x3C, // ㅒ  점3,4,5,6
            0x16, // ㅓ  점2,3,5
            0x32, // ㅔ  점2,5,6
            0x31, // ㅕ  점1,5,6
            0x33, // ㅖ  점1,2,5,6
            0x15, // ㅗ  점1,3,5
            0xFF, // ㅘ  두 셀
            0xFF, // ㅙ  두 셀
            0xFF, // ㅚ  두 셀
            0x17, // ㅛ  점1,2,3,5
            0x36, // ㅜ  점2,3,5,6
            0xFF, // ㅝ  두 셀
            0xFF, // ㅞ  두 셀
            0xFF, // ㅟ  두 셀
            0x35, // ㅠ  점1,3,5,6
            0x34, // ㅡ  점3,5,6
            0xFF, // ㅢ  두 셀
            0x25, // ㅣ  점1,3,6
        };

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

        // 종성 (28개: 0=없음, 0xFF=겹받침→CompoundFinal)
        private static readonly byte[] FinalConsonant = {
            0x00, // 0  없음
            0x08, // 1  ㄱ
            0x28, // 2  ㄲ
            0xFF, // 3  ㄳ
            0x09, // 4  ㄴ
            0xFF, // 5  ㄵ
            0xFF, // 6  ㄶ
            0x0A, // 7  ㄷ
            0x1B, // 8  ㄹ
            0xFF, // 9  ㄺ
            0xFF, // 10 ㄻ
            0xFF, // 11 ㄼ
            0xFF, // 12 ㄽ
            0xFF, // 13 ㄾ
            0xFF, // 14 ㄿ
            0xFF, // 15 ㅀ
            0x19, // 16 ㅁ
            0x0B, // 17 ㅂ
            0xFF, // 18 ㅄ
            0x0D, // 19 ㅅ
            0x2D, // 20 ㅆ
            0x24, // 21 ㅇ (받침 전용: 점3,6)
            0x1A, // 22 ㅈ
            0x39, // 23 ㅊ
            0x0F, // 24 ㅋ
            0x1E, // 25 ㅌ
            0x1D, // 26 ㅍ
            0x33, // 27 ㅎ
        };

        private static readonly Dictionary<int, (byte A, byte B)> CompoundFinal = new()
        {
            {  3, (0x08, 0x0D) }, // ㄳ = ㄱ+ㅅ
            {  5, (0x09, 0x1A) }, // ㄵ = ㄴ+ㅈ
            {  6, (0x09, 0x33) }, // ㄶ = ㄴ+ㅎ
            {  9, (0x1B, 0x08) }, // ㄺ = ㄹ+ㄱ
            { 10, (0x1B, 0x19) }, // ㄻ = ㄹ+ㅁ
            { 11, (0x1B, 0x0B) }, // ㄼ = ㄹ+ㅂ
            { 12, (0x1B, 0x0D) }, // ㄽ = ㄹ+ㅅ
            { 13, (0x1B, 0x1E) }, // ㄾ = ㄹ+ㅌ
            { 14, (0x1B, 0x1D) }, // ㄿ = ㄹ+ㅍ
            { 15, (0x1B, 0x33) }, // ㅀ = ㄹ+ㅎ
            { 18, (0x0B, 0x0D) }, // ㅄ = ㅂ+ㅅ
        };

        #endregion

        // ══════════════════════════════════════════════════════════════════
        // 영문·숫자 코드 (Grade 1 Braille)
        // ══════════════════════════════════════════════════════════════════
        #region Latin / Number Tables

        // 영문 소문자 a~z
        private static readonly byte[] LatinLower = {
            0x01, // a  점1
            0x03, // b  점1,2
            0x09, // c  점1,4
            0x19, // d  점1,4,5
            0x11, // e  점1,5
            0x0B, // f  점1,2,4
            0x1B, // g  점1,2,4,5
            0x13, // h  점1,2,5
            0x0A, // i  점2,4
            0x1A, // j  점2,4,5
            0x05, // k  점1,3
            0x07, // l  점1,2,3
            0x0D, // m  점1,3,4
            0x1D, // n  점1,3,4,5
            0x15, // o  점1,3,5
            0x0F, // p  점1,2,3,4
            0x1F, // q  점1,2,3,4,5
            0x17, // r  점1,2,3,5
            0x0E, // s  점2,3,4
            0x1E, // t  점2,3,4,5
            0x25, // u  점1,3,6
            0x27, // v  점1,2,3,6
            0x3A, // w  점2,4,5,6
            0x2D, // x  점1,3,4,6
            0x3D, // y  점1,3,4,5,6
            0x35, // z  점1,3,5,6
        };

        // 숫자 0~9 (숫자 표시자 뒤에 따름)
        private static readonly byte[] DigitCodes = {
            0x1A, // 0 (j 패턴)
            0x01, // 1 (a 패턴)
            0x03, // 2 (b 패턴)
            0x09, // 3 (c 패턴)
            0x19, // 4 (d 패턴)
            0x11, // 5 (e 패턴)
            0x0B, // 6 (f 패턴)
            0x1B, // 7 (g 패턴)
            0x13, // 8 (h 패턴)
            0x0A, // 9 (i 패턴)
        };

        private static readonly Dictionary<char, byte> PunctuationCodes = new()
        {
            { '.', 0x04 }, // 마침표:   점3
            { ',', 0x02 }, // 쉼표:     점2
            { '?', 0x26 }, // 물음표:   점2,3,6
            { '!', 0x16 }, // 느낌표:   점2,3,5
            { '-', 0x24 }, // 하이픈:   점3,6
            { ':', 0x12 }, // 콜론:     점2,5
            { ';', 0x06 }, // 세미콜론: 점2,3
            { '(', 0x2E }, // 괄호 열기: 점2,3,4,6
            { ')', 0x2E }, // 괄호 닫기: 점2,3,4,6
        };

        #endregion
    }
}
