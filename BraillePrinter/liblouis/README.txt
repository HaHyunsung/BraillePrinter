═══════════════════════════════════════════════════════════════
 liblouis 설치 가이드 — BraillePrinter 프로젝트
═══════════════════════════════════════════════════════════════

■ 이 폴더의 역할
  앱 실행 시 liblouis 엔진을 사용하려면 이 폴더에
  DLL 파일과 테이블 파일을 배치해야 합니다.

■ 필요한 파일 구조
  liblouis\
    liblouis.dll          ← Windows 네이티브 DLL
    tables\
      ko-g2.ctb           ← 한국어 약자 (기본)
      ko-g1.ctb           ← 한국어 정자
      ko-g2-rules.cti     ← 한국어 약자 규칙 (ko-g2.ctb가 include)
      ko-g2-chars.cti     ← 한국어 약자 문자 정의
      ko.ctb              ← 한국어 공통 기반 테이블
      braille-patterns.cti
      (그 외 ko-g2.ctb가 include하는 파일들)

■ 다운로드 방법

  방법 1) liblouis GitHub Releases (권장)
    URL: https://github.com/liblouis/liblouis/releases
    → 최신 릴리즈의 Assets에서 Windows 빌드를 다운로드
    → 또는 직접 빌드 (cmake + Visual Studio 필요)

  방법 2) NVDA 스크린리더에서 추출
    NVDA 설치 후 아래 경로에서 파일 복사:
      C:\Program Files (x86)\NVDA\liblouis\
    → liblouis.dll과 tables\ 폴더 전체를 이 폴더로 복사

  방법 3) 미리 빌드된 바이너리
    https://github.com/nvaccess/nvda/tree/master/source/louis
    (NVDA 소스코드 저장소, MIT 라이선스로 배포)

■ 설치 확인
  앱 실행 후 파라미터 설정 창에서
  변환 엔진 섹션의 "liblouis 상태"가 "✔ 사용 가능"으로
  표시되면 정상 설치된 것입니다.

■ 테이블 선택
  ko-g2.ctb : 약자(약어) 포함 — 출판용 점자에 적합
  ko-g1.ctb : 정자(풀어쓰기) — 학습용, 1:1 대응

■ 라이선스
  liblouis: LGPL-2.1
  한국어 테이블: liblouis 프로젝트 기여자
═══════════════════════════════════════════════════════════════
