using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace BraillePrinter.Converters
{
    /// <summary>
    /// liblouis 오픈소스 라이브러리를 DLL P/Invoke로 호출하는 변환기.
    ///
    /// 특징:
    ///   - 한국어 정자(ko-g1.ctb) / 약자(ko-g2.ctb) 모두 지원
    ///   - 영문, 숫자, 구두점, 혼합 언어 자동 처리
    ///   - 라이브러리 규정 개정 시 DLL + 테이블 파일 교체만으로 대응
    ///
    /// DLL 배치 위치 (우선순위 순):
    ///   1. {앱실행경로}\liblouis\liblouis.dll
    ///   2. {앱실행경로}\liblouis.dll
    ///
    /// 헤더 확인 사항 (liblouis-3.37.0-win64):
    ///   - widechar = unsigned int  (4바이트)
    ///   - EXPORT_CALL  = __stdcall (Windows)
    ///   - 테이블 탐색 우선순위: 절대경로 > LOUIS_TABLEPATH 환경변수
    ///
    /// 사용 불가 시 ManualBrailleConverter로 자동 대체됩니다.
    /// </summary>
    public class LibLouisConverter : IBrailleConverter
    {
        // ── 공개 속성 ─────────────────────────────────────────────────────

        public string Name        => "liblouis";
        public string Description => "liblouis 라이브러리 (한국어 약자·영문·수학 완전 지원)";

        public bool IsAvailable
        {
            get
            {
                _isAvailable ??= CheckAvailability();
                return _isAvailable.Value;
            }
        }

        public string UnavailableReason => _unavailableReason;

        /// <summary>
        /// 사용할 liblouis 테이블.
        /// 한국어 정자: "ko-g1.ctb" / 한국어 약자: "ko-g2.ctb"
        /// </summary>
        public string TableName { get; set; } = "ko-g2.ctb";

        /// <summary>마지막 변환 오류 메시지 (null이면 정상)</summary>
        public string? LastError { get; private set; }

        // ── 내부 상태 ─────────────────────────────────────────────────────

        private bool?  _isAvailable;
        private string _unavailableReason = string.Empty;
        private bool   _initialized;

        // ── 싱글톤 인스턴스 ───────────────────────────────────────────────

        public static readonly LibLouisConverter Instance = new();

        // ── DLL 이름 ──────────────────────────────────────────────────────

        private const string DllName = "liblouis.dll";

        // ── 로그 경로 (프로젝트 내 Logs 폴더) ────────────────────────────

        private static readonly string LogPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "Logs", "liblouis.log");

        // ── 정적 초기화: DLL 경로 해석기 등록 ────────────────────────────

        static LibLouisConverter()
        {
            // !! LOUIS_TABLEPATH는 DLL 로드보다 반드시 먼저 설정해야 합니다 !!
            // liblouis는 최초 테이블 로드 시 이 경로를 사용해 #include 파일을 탐색합니다.
            // EnsureInitialized()에서 설정하면 이미 DLL이 로드된 후라서 무시됩니다.
            string appDir    = AppDomain.CurrentDomain.BaseDirectory;
            string tablesDir = Path.Combine(appDir, "liblouis", "tables");
            if (!Directory.Exists(tablesDir))
                tablesDir = Path.Combine(appDir, "tables");

            if (Directory.Exists(tablesDir))
            {
                Environment.SetEnvironmentVariable("LOUIS_TABLEPATH", tablesDir);
                // 로그는 아직 쓸 수 없으므로 파일에 직접 기록
                try
                {
                    string logDir = Path.Combine(appDir, "Logs");
                    Directory.CreateDirectory(logDir);
                    File.AppendAllText(
                        Path.Combine(logDir, "liblouis.log"),
                        $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [LibLouis]  [STATIC] LOUIS_TABLEPATH = {tablesDir}{Environment.NewLine}");
                }
                catch { }
            }

            NativeLibrary.SetDllImportResolver(
                Assembly.GetExecutingAssembly(), ResolveDll);
        }

        private static IntPtr ResolveDll(string libraryName,
                                         Assembly assembly,
                                         DllImportSearchPath? searchPath)
        {
            if (!libraryName.Equals(DllName, StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates =
            {
                Path.Combine(appDir, "liblouis", DllName),
                Path.Combine(appDir, DllName),
            };

            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                if (NativeLibrary.TryLoad(path, out IntPtr handle))
                    return handle;
            }

            return IntPtr.Zero;
        }

        // ── P/Invoke 선언 ─────────────────────────────────────────────────
        //
        // widechar = unsigned int (4바이트) — liblouis.h 40번 줄 확인
        // EXPORT_CALL = __stdcall (Windows) — liblouis.h 58번 줄 확인
        //
        // 따라서:
        //   - CharSet.Unicode / MarshalAs 사용 금지 (직접 uint[] 핀 고정)
        //   - CallingConvention.StdCall

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_charSize")]
        private static extern int lou_charSize();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_translateString")]
        private static extern int lou_translateString(
            string tableList,
            IntPtr inbuf,  ref int inlen,
            IntPtr outbuf, ref int outlen,
            IntPtr typeform,
            IntPtr spacing,
            int    mode);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_setDataPath")]
        private static extern void lou_setDataPath(string path);

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_getDataPath")]
        private static extern IntPtr lou_getDataPath();

        [DllImport(DllName, CallingConvention = CallingConvention.StdCall,
                   EntryPoint = "lou_free")]
        private static extern void lou_free();

        // ── 공개 변환 API ─────────────────────────────────────────────────

        public List<byte> Convert(string text)
        {
            EnsureInitialized();
            LastError = null;

            var patterns = new List<byte>();

            // liblouis는 줄 단위로 처리 → 줄바꿈 기준으로 분리
            var lines = text.Replace("\r\n", "\n").Split('\n');

            for (int i = 0; i < lines.Length; i++)
            {
                if (i > 0) patterns.Add(0xFE);  // 줄바꿈 마커

                string line = lines[i];
                if (line.Length == 0) continue;

                var linePatterns = new List<byte>();
                TranslateLine(line, linePatterns);

                // ── 영어 개방/종료 마커 삽입 ─────────────────────────────────
                // ko-g2 테이블은 letsign(dot6=0x20)을 영어 앞에 삽입하지만
                // 한국 점자 표준 영문 phrase marker(⠴=0x34, ⠲=0x32)는 생성 안 함.
                // 후처리로 삽입: letsign(0x20) 직전에 영어개방표시(0x34) 추가,
                //                연속된 영어 구간이 끝난 뒤 영어종료표시(0x32) 추가.
                patterns.AddRange(InjectEnglishMarkers(line, linePatterns));
            }

            return patterns;
        }

        /// <summary>
        /// liblouis 출력 패턴에 한국 점자 규정의 '영어 표시'(⠴ = dots3,5,6 = 0x34)를 삽입합니다.
        ///
        /// 한국 점자 규정: 한국어 문맥에서 영어 단어/구 앞에 영어 표시(점3-5-6)를 붙임.
        /// liblouis ko-g2.ctb 는 capsletter(dot6=⠠)만 생성하고 영어 표시를 생성하지 않음.
        ///
        /// ⠲(dots2,5,6=0x32)는 영어 종료가 아니라 마침표(.) — liblouis가 자연 처리.
        /// 따라서 이 메서드는 ⠴만 삽입하며 종료 표시는 삽입하지 않습니다.
        /// </summary>
        private static List<byte> InjectEnglishMarkers(string originalLine, List<byte> src)
        {
            // 영어 알파벳이 없으면 그대로 반환
            bool hasEnglish = originalLine.Any(c =>
                (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'));
            if (!hasEnglish) return src;

            const byte CAPSLETTER = 0x20;   // ⠠ dot 6 — 대문자 표시 (ko.cti: capsletter 6)
            const byte ENG_OPEN   = 0x34;   // ⠴ dots 3,5,6 — 영어 표시 (한국 점자 규정)

            var result  = new List<byte>(src.Count + 2);
            bool engStarted = false;

            for (int i = 0; i < src.Count; i++)
            {
                byte p = src[i];

                // capsletter(0x20) 또는 영어 소문자 패턴(0x01~0x1F) 시작 시
                // '영어 표시(⠴)' 삽입 (아직 한 번도 삽입 안 했을 때)
                if (!engStarted && p == CAPSLETTER)
                {
                    result.Add(ENG_OPEN);
                    engStarted = true;
                }

                result.Add(p);
            }

            return result;
        }

        // ── 내부 구현 ─────────────────────────────────────────────────────

        private void TranslateLine(string line, List<byte> patterns)
        {
            // widechar = uint (4바이트): UTF-32 코드포인트 배열로 변환
            uint[] inArr  = line.Select(c => (uint)c).ToArray();
            int    inlen  = inArr.Length;

            // 출력 버퍼: 입력 대비 충분히 여유 확보 (약자는 확장될 수 있음)
            int outCapacity = Math.Max(inlen * 8, 64);
            int outlen      = outCapacity;

            // 절대경로로 테이블 지정 (LOUIS_TABLEPATH 환경변수로 설정된 경로 + 파일명)
            string tableList = ResolveTablePath();

            GCHandle inHandle = GCHandle.Alloc(inArr, GCHandleType.Pinned);
            // outbuf: widechar(uint=4바이트) * outCapacity 바이트
            IntPtr pOut = Marshal.AllocHGlobal(outCapacity * sizeof(uint));

            int result;
            try
            {
                result = lou_translateString(
                    tableList,
                    inHandle.AddrOfPinnedObject(), ref inlen,
                    pOut,                          ref outlen,
                    IntPtr.Zero, IntPtr.Zero, 0);

                if (result <= 0)
                {
                    LastError = $"변환 실패 (result={result}), 테이블: {tableList}";
                    LogDebug($"FAIL  result={result}  inlen={inlen}  outlen={outlen}  table={tableList}  input=\"{line}\"");
                    return;
                }

                LastError = null;

                // ── 출력값 hex 덤프 (진단용) ───────────────────────────────
                var hexDump = new System.Text.StringBuilder();
                int showCount = Math.Min(outlen, 32);
                for (int i = 0; i < showCount; i++)
                    hexDump.Append($"{(uint)Marshal.ReadInt32(pOut, i * sizeof(uint)):X8} ");
                LogDebug($"OK    result={result}  inlen={inlen}  outlen={outlen}  hex=[{hexDump.ToString().TrimEnd()}]");
                // ───────────────────────────────────────────────────────────

                int addedCount = 0;
                var caseSummary = new System.Text.StringBuilder();

                for (int i = 0; i < outlen; i++)
                {
                    uint val = (uint)Marshal.ReadInt32(pOut, i * sizeof(uint));

                    if (val >= 0x2800u && val <= 0x28FFu)
                    {
                        // ▶ 케이스 A: Unicode 점자 (U+2800+) — 하위 8비트가 dot bitmask
                        byte p = (byte)(val & 0xFFu);
                        patterns.Add(p);
                        caseSummary.Append($"A({val:X4}→{p:X2}) ");
                        addedCount++;
                    }
                    else if (val >= 0x20u && val <= 0xFFu)
                    {
                        // ▶ 케이스 B: BRF (Braille Ready Format) ASCII 인코딩
                        // liblouis ko-g2 테이블이 BRF 인코딩으로 출력함
                        byte p = BrfToPattern((char)(val & 0x7Fu));
                        patterns.Add(p);
                        caseSummary.Append($"B({val:X2}→{p:X2}) ");
                        addedCount++;
                    }
                    else if ((val & 0x8000u) != 0)
                    {
                        // ▶ 케이스 C: LOU_DOTS 플래그 포함 — 하위 8비트가 dot bitmask
                        byte p = (byte)(val & 0xFFu);
                        patterns.Add(p);
                        caseSummary.Append($"C({val:X8}→{p:X2}) ");
                        addedCount++;
                    }
                    // 0x00~0x1F, 기타는 무시

                }

                LogDebug($"      [{addedCount}] {caseSummary.ToString().TrimEnd()}");

            }
            catch (Exception ex)
            {
                LastError = $"네이티브 예외: {ex.GetType().Name} — {ex.Message}";
                LogDebug($"EXCEPTION {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                inHandle.Free();
                Marshal.FreeHGlobal(pOut);
            }
        }

        /// <summary>
        /// BRF(Braille Ready Format) ASCII → 6-dot 비트마스크 변환.
        ///
        /// 로그 검증 (안녕하세요 → ⠣⠒⠉⠻⠚⠠⠝⠬):
        ///   liblouis 출: [3C 33 63 7D 6A 2C 6E 2B]
        ///   기대 패턴:   [23 12 09 3B 1A 20 1D 2C]
        /// 패턴: '<'(3C)→23, '3'(33)→12, 'c'(63)→09, '}'(7D)→3B,
        ///       'j'(6A)→1A, ','(2C)→20, 'n'(6E)→1D, '+'(2B)→2C
        ///
        /// 이것은 NABCC 표준의 역방향 매핑(dot-bitmask → ASCII 문자)의 역산입니다.
        /// bit0=점1, bit1=점2, bit2=점3, bit3=점4, bit4=점5, bit5=점6
        /// </summary>
        private static byte BrfToPattern(char c)
        {
            // 로그 검증된 BRF(NABCC) 역-변환 테이블: ASCII 문자 → dot 비트마스크
            // NABCC 표준의 DOT→ASCII 매핑을 역산하여 ASCII→DOT 로 재구성
            // (dot N에 대응하는 ASCII 문자의 역방향)
            // 검증: ','(0x2C)→0x20✓, '+'(0x2B)→0x2C✓, '3'(0x33)→0x12✓,
            //       '<'(0x3C)→0x23✓, 'c'(0x63)→0x09✓, '}'(0x7D)→0x3B✓,
            //       'j'(0x6A)→0x1A✓, 'n'(0x6E)→0x1D✓
            ReadOnlySpan<byte> table = stackalloc byte[]
            {
                // 0x20=' '  0x21='!'  0x22='"'  0x23='#'  0x24='$'  0x25='%'  0x26='&'  0x27='\''
                   0x00,     0x2E,     0x10,     0x3C,     0x2B,     0x29,     0x2F,     0x04,
                // 0x28='('  0x29=')'  0x2A='*'  0x2B='+'  0x2C=','  0x2D='-'  0x2E='.'  0x2F='/'
                   0x37,     0x3E,     0x21,     0x2C,     0x20,     0x24,     0x28,     0x0C,
                // 0x30='0'  0x31='1'  0x32='2'  0x33='3'  0x34='4'  0x35='5'  0x36='6'  0x37='7'
                   0x34,     0x02,     0x06,     0x12,     0x32,     0x22,     0x16,     0x36,
                // 0x38='8'  0x39='9'  0x3A=':'  0x3B=';'  0x3C='<'  0x3D='='  0x3E='>'  0x3F='?'
                   0x26,     0x14,     0x31,     0x30,     0x23,     0x3F,     0x1C,     0x39,
                // 0x40='@'  0x41='A'  0x42='B'  0x43='C'  0x44='D'  0x45='E'  0x46='F'  0x47='G'
                   0x08,     0x01,     0x03,     0x09,     0x19,     0x11,     0x0B,     0x1B,
                // 0x48='H'  0x49='I'  0x4A='J'  0x4B='K'  0x4C='L'  0x4D='M'  0x4E='N'  0x4F='O'
                   0x13,     0x0A,     0x1A,     0x05,     0x07,     0x0D,     0x1D,     0x15,
                // 0x50='P'  0x51='Q'  0x52='R'  0x53='S'  0x54='T'  0x55='U'  0x56='V'  0x57='W'
                   0x0F,     0x1F,     0x17,     0x0E,     0x1E,     0x25,     0x27,     0x3A,
                // 0x58='X'  0x59='Y'  0x5A='Z'  0x5B='['  0x5C='\'  0x5D=']'  0x5E='^'  0x5F='_'
                   0x2D,     0x3D,     0x35,     0x2A,     0x33,     0x3B,     0x18,     0x38,
                // 0x60='`'  0x61='a'  0x62='b'  0x63='c'  0x64='d'  0x65='e'  0x66='f'  0x67='g'
                   0x00,     0x01,     0x03,     0x09,     0x19,     0x11,     0x0B,     0x1B,
                // 0x68='h'  0x69='i'  0x6A='j'  0x6B='k'  0x6C='l'  0x6D='m'  0x6E='n'  0x6F='o'
                   0x13,     0x0A,     0x1A,     0x05,     0x07,     0x0D,     0x1D,     0x15,
                // 0x70='p'  0x71='q'  0x72='r'  0x73='s'  0x74='t'  0x75='u'  0x76='v'  0x77='w'
                   0x0F,     0x1F,     0x17,     0x0E,     0x1E,     0x25,     0x27,     0x3A,
                // 0x78='x'  0x79='y'  0x7A='z'  0x7B='{'  0x7C='|'  0x7D='}'  0x7E='~'
                   0x2D,     0x3D,     0x35,     0x2A,     0x33,     0x3B,     0x18,
            };

            int idx = c - 0x20;
            if (idx < 0 || idx >= table.Length) return 0x00;
            return table[idx];
        }

        /// <summary>
        /// 테이블 절대경로를 반환합니다.
        /// 파일이 존재하면 절대경로를, 없으면 파일명만 반환합니다 (liblouis 기본 경로 탐색).
        /// </summary>
        private string ResolveTablePath()
        {
            string appDir   = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath = Path.Combine(appDir, "liblouis", "tables", TableName);
            if (File.Exists(fullPath)) return fullPath;

            // 루트 tables 폴더 확인
            string rootTable = Path.Combine(appDir, "tables", TableName);
            if (File.Exists(rootTable)) return rootTable;

            return TableName;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;

            string appDir = AppDomain.CurrentDomain.BaseDirectory;

            // lou_setDataPath: liblouis가 #include 파일 탐색에 사용하는 루트 경로
            // liblouis는 내부적으로 {dataPath}/tables/{filename} 형식으로 파일을 참조함
            // → dataPath = liblouis 루트 (tables 폴더의 부모)
            string louRootDir;
            string subTables = Path.Combine(appDir, "liblouis", "tables");
            if (Directory.Exists(subTables))
                louRootDir = Path.Combine(appDir, "liblouis");
            else
                louRootDir = appDir;

            try
            {
                lou_setDataPath(louRootDir);
                IntPtr pPath = lou_getDataPath();
                string? actualPath = Marshal.PtrToStringAnsi(pPath);
                LogDebug($"lou_setDataPath({louRootDir})  →  getDataPath={actualPath}");
            }
            catch (Exception ex)
            {
                LogDebug($"lou_setDataPath 호출 실패: {ex.Message}");
            }

            // widechar 크기 확인 (헤더: unsigned int = 4바이트이어야 함)
            try
            {
                int charSize = lou_charSize();
                LogDebug($"lou_charSize() = {charSize} bytes (예상: 4)");
                if (charSize != 4)
                    LogDebug($"WARNING: widechar 크기가 4바이트가 아닙니다! P/Invoke 재검토 필요.");
            }
            catch (Exception ex)
            {
                LogDebug($"lou_charSize() 호출 실패: {ex.Message}");
            }

            _initialized = true;
        }

        private bool CheckAvailability()
        {
            try
            {
                lou_charSize();   // DLL 로드 확인용 (lou_charSize가 더 안전)
                return true;
            }
            catch (DllNotFoundException ex)
            {
                _unavailableReason =
                    $"liblouis.dll을 찾을 수 없습니다.\n" +
                    $"앱 폴더의 liblouis\\ 또는 루트에 DLL과 tables\\ 폴더를 배치하세요.\n" +
                    $"({ex.Message})";
                return false;
            }
            catch (Exception ex)
            {
                _unavailableReason = $"liblouis 초기화 오류: {ex.Message}";
                return false;
            }
        }

        // ── 로그 ─────────────────────────────────────────────────────────

        private static void LogDebug(string message)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                File.AppendAllText(LogPath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}  [LibLouis]  {message}{Environment.NewLine}");
            }
            catch { /* 로그 실패는 무시 */ }
        }
    }
}
