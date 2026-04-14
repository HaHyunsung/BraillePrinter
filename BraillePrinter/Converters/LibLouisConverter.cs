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

        private bool? _isAvailable;
        private string _unavailableReason = string.Empty;
        private bool _initialized;

        // ── 싱글톤 인스턴스 ───────────────────────────────────────────────

        public static readonly LibLouisConverter Instance = new();

        // ── DLL 이름 ──────────────────────────────────────────────────────

        private const string DllName = "liblouis.dll";

        // ── 정적 초기화: DLL 경로 해석기 등록 ────────────────────────────

        static LibLouisConverter()
        {
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

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_setDataPath")]
        private static extern void lou_setDataPath(string path);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   EntryPoint = "lou_getDataPath")]
        private static extern IntPtr lou_getDataPath();

        /// <summary>
        /// 원시 포인터 기반 P/Invoke — 관리 배열 마샬링 우회.
        /// inbuf/outbuf 모두 IntPtr로 받아 GCHandle 핀/AllocHGlobal로 직접 전달.
        /// </summary>
        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
                   CharSet = CharSet.Ansi, EntryPoint = "lou_translateString")]
        private static extern int lou_translateString(
            string tableList,
            IntPtr inbuf,  ref int inlen,
            IntPtr outbuf, ref int outlen,
            IntPtr typeform,
            IntPtr spacing,
            int    mode);

        [DllImport(DllName, CallingConvention = CallingConvention.Cdecl,
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

                TranslateLine(line, patterns);
            }

            return patterns;
        }

        // ── 내부 구현 ─────────────────────────────────────────────────────

        private void TranslateLine(string line, List<byte> patterns)
        {
            // UTF-16 ushort 배열로 변환 (liblouis widechar = unsigned short)
            ushort[] inArr = line.Select(c => (ushort)c).ToArray();
            int inlen      = inArr.Length;

            int outCapacity = inlen * 4;          // 여유 있게 4배
            int outlen      = outCapacity;

            string tableList = ResolveTablePath();

            // inbuf: 관리 배열을 핀 고정하여 네이티브 포인터로 전달
            GCHandle inHandle  = GCHandle.Alloc(inArr, GCHandleType.Pinned);
            // outbuf: 네이티브 힙에 직접 할당 (관리 마샬링 완전 우회)
            IntPtr   pOut      = Marshal.AllocHGlobal(outCapacity * sizeof(ushort));

            int result;
            try
            {
                result = lou_translateString(
                    tableList,
                    inHandle.AddrOfPinnedObject(), ref inlen,
                    pOut,                          ref outlen,
                    IntPtr.Zero, IntPtr.Zero, 0);

                // ── 진단 로그 (임시) ──────────────────────────────────────
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"[LibLouis] result={result}  inlen={inlen}  outlen={outlen}");
                sb.AppendLine($"  table={tableList}");
                sb.AppendLine($"  input=\"{line}\"");
                if (result > 0)
                {
                    sb.Append("  outbuf hex:");
                    int showCount = Math.Min(outlen, 30);
                    for (int i = 0; i < showCount; i++)
                    {
                        ushort v = (ushort)Marshal.ReadInt16(pOut, i * 2);
                        sb.Append($" {v:X4}");
                    }
                    sb.AppendLine();
                }
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                    "liblouis_debug.txt");
                File.AppendAllText(logPath, sb.ToString());
                // ──────────────────────────────────────────────────────────

                if (result <= 0)
                {
                    LastError = $"변환 실패 (result={result}), 테이블: {tableList}";
                    return;
                }

                LastError = null;

                // 출력 ushort 배열을 유니코드 점자(U+2800~U+28FF)로 해석
                for (int i = 0; i < outlen; i++)
                {
                    char ch = (char)(ushort)Marshal.ReadInt16(pOut, i * 2);

                    if (ch >= '\u2800' && ch <= '\u28FF')
                    {
                        byte pattern = (byte)((ch - 0x2800) & 0x3F);
                        patterns.Add(pattern);
                    }
                }
            }
            finally
            {
                inHandle.Free();
                Marshal.FreeHGlobal(pOut);
            }
        }

        /// <summary>
        /// 테이블 파일의 절대경로를 반환합니다.
        /// 파일이 존재하면 절대경로를, 없으면 원래 TableName을 그대로 반환합니다.
        /// </summary>
        private string ResolveTablePath()
        {
            string appDir    = AppDomain.CurrentDomain.BaseDirectory;
            string fullPath  = Path.Combine(appDir, "liblouis", "tables", TableName);
            return File.Exists(fullPath) ? fullPath : TableName;
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            string appDir   = AppDomain.CurrentDomain.BaseDirectory;
            string subPath  = Path.Combine(appDir, "liblouis", "tables");
            string rootPath = Path.Combine(appDir, "tables");

            if (Directory.Exists(subPath))
                lou_setDataPath(Path.Combine(appDir, "liblouis"));
            else if (Directory.Exists(rootPath))
                lou_setDataPath(appDir);
        }

        private bool CheckAvailability()
        {
            try
            {
                lou_getDataPath();
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
    }
}
