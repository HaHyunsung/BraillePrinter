using System.Windows;
using BraillePrinter.Services;

namespace BraillePrinter.Views
{
    public partial class AlarmDialog : Window
    {
        /// <summary>
        /// 사용자가 "리셋 &amp; 홈" 버튼을 눌렀는지 여부.
        /// 호출자(MainWindow)가 ShowDialog 후 이 값을 보고 HomeAsync()를 실행합니다.
        /// </summary>
        public bool ShouldRunHome { get; private set; }

        public AlarmDialog(int alarmCode)
        {
            InitializeComponent();

            TbAlarmTitle.Text = alarmCode > 0
                ? $"ALARM:{alarmCode} 발생"
                : "ALARM 상태 감지됨";

            TbAlarmDesc.Text = GetAlarmDescription(alarmCode);
        }

        private static string GetAlarmDescription(int code) => code switch
        {
            1 => "하드 리밋 스위치 작동 (Hard limit triggered)\n"
                 + "축이 기계 한계점에 닿았거나 리밋 스위치가 트리거되었습니다.",
            2 => "이동 명령이 기계 범위를 초과합니다 (Soft limit).\n"
                 + "G-Code의 목표 좌표가 작업 영역을 벗어났습니다.",
            3 => "이동 중 리셋이 발생했습니다 (Reset while in motion).\n"
                 + "위치 정보 신뢰성이 손상되어 홈잉이 필요합니다.",
            4 => "프로브 실패 — 초기 접촉 상태 오류 (Probe fail).",
            5 => "프로브 실패 — 프로브가 접촉하지 않았습니다 (Probe fail).",
            6 => "홈잉 실패 — 사이클 중 리셋이 발생했습니다.",
            7 => "홈잉 실패 — 사이클 중 안전 도어가 열렸습니다.",
            8 => "홈잉 실패 — 리밋 스위치를 벗어나지 못했습니다.\n"
                 + "리밋 스위치가 눌린 상태일 가능성이 있습니다.",
            9 => "홈잉 실패 — 리밋 스위치를 찾지 못했습니다.\n"
                 + "배선 또는 스위치 동작을 확인하세요.",
            _ => "GRBL이 잠금(Alarm) 상태입니다.\n"
                 + "구체적인 원인 코드를 수신하지 못했습니다 — 통신 로그를 확인하세요."
        };

        private void BtnResetHome_Click(object sender, RoutedEventArgs e)
        {
            // Soft Reset만 여기서 보내고, Home은 호출자(MainWindow)가 실행합니다.
            // (HomeAsync는 async이므로 다이얼로그 닫힘 후에 처리하는 것이 안전)
            GrblService.Instance.SoftReset();
            ShouldRunHome = true;
            DialogResult = true;
            Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            ShouldRunHome = false;
            DialogResult = false;
            Close();
        }
    }
}
