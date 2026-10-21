# Xapper 격리 launch·입력 개선 구현 및 검증 기록

작성일: 2026-10-21

코드와 회귀 테스트를 구현하고 Linux에서 전체 솔루션 교차 빌드를 확인했다.
Windows 실행 검증은 완료하지 못했다. 아래에서 구현과 실행 증거를 구분한다.

## A. 격리 데스크톱 launch

| 파일 | 변경 |
| --- | --- |
| `src/Xapper.Protocol/IsolatedDesktopEnvironment.cs` | 서버와 Inspector가 공유하는 `XAPPER_ISOLATED` 상수. |
| `src/Xapper.McpServer/Infrastructure/IsolatedDesktopApp.cs` | 고유 Desktop 생성, Unicode 환경 블록, suspended 프로세스 생성, kill-on-close Job 배치 후 재개. Desktop·Job·Process 핸들을 수명 동안 소유하고 실패 시 정리. |
| `src/Xapper.McpServer/SessionManager.cs`, `InspectorSession.cs` | 연결과 격리 소유권을 함께 등록. 클라이언트·PID·격리 여부의 불변 스냅샷 제공. detach 및 전체 dispose 시 앱과 자식 정리. |
| `src/Xapper.McpServer/Tools/ProcessTools.cs` | `isolated=false` 인자, 격리 실행, 연결 직후 소유권 이전, 클라이언트별 빌드 확인, 실패·취소 시 해당 PID 정리, 격리 detach의 종료 응답. |
| `src/Xapper.Injector/WpfProcessInjector.cs` | .NET Framework 격리 앱은 소유 Desktop에서 찾은 HWND의 PID를 확인한 뒤 기존 네이티브 주입 경로 사용. 일반 attach의 창 검증 유지. |
| `src/Xapper.Inspector/IsolatedMode.cs` | 환경 값 `"1"`만 활성화. 실제 입력·screen 캡처·전경 전환 거절 문구 제공. |
| `src/Xapper.Inspector/IpcServer.cs`, `Actions/ClickAction.cs`, `NativeWindows/WindowCommands.cs` | 성공한 합성 입력은 유지하고 실제 입력 폴백 직전 거절. screen 캡처와 front 명령을 부작용 전에 차단. |
| 서버 `RecordTools.cs`, `NoticeTools.cs`, `RunTools.cs` | 격리 세션의 데스크톱 녹화 거절, 알림 생략, 스크립트 알림 억제, 자동 실패 스크린샷은 render 사용. |

이 저장소에 맞춘 판단:

- 일반 launch는 MCP stdout 보호를 위해 출력 파이프를 계속 읽는다. 해당 Process를
  메서드 끝에서 dispose하는 예시 코드는 적용하지 않았다.
- .NET Framework는 startup hook이 없고 서버에서 MainWindowHandle이 0이므로,
  소유한 Desktop의 창을 열거하는 별도 시작 주입 경로를 구현했다.
- 녹화는 경고 후 실행하지 않고 거절한다. 기존 녹화기는 사람 Desktop을 녹화하므로
  격리 앱이 기록됐다는 오해를 만들기 때문이다. 녹화 종료 명령은 계속 사용할 수 있다.
- 스크립트의 알림과 실패 스크린샷도 같은 격리 정책을 적용한다.

## B. 클릭·키 입력

| 파일 | 변경 |
| --- | --- |
| `Actions/ClickAction.cs`, `IpcServer.cs`, `Actions/ClickOutcome.cs` | Invoke·Toggle·미선택 항목 Select·버튼 이벤트 경로를 판별. 경로가 없으면 가운데 합성 클릭. 네 개의 직접 라우티드 마우스 이벤트 제거. 버튼은 Hover 외에는 활성화 전 포커스를 받음. 오류를 명시적 결과로 전달. |
| `Actions/ClickAction.cs` | 직접 호출에서도 별도 송신 스레드와 Dispatcher 펌프를 사용해 느린 핸들러 동안 SendMessageTimeout 감시가 작동하도록 함. |
| `Actions/KeyboardInput.cs` | Alt(Ctrl 없음)·F10(Ctrl 없음)을 시스템 키로 표시. StartComposition 사용, 처리된 key-down의 문자 억제, 현재 포커스로 문자 전달, VkKeyScanW로 배열별 키 매핑. |
| `Actions/InputSpoof.cs` | 주입 중인 가상 키의 눌림 상태를 해당 스레드에 한정. key-up 전과 finally에서 해제. 수식키 상태도 주입 스레드에 한정. |

Ctrl/Alt 문자 단축키는 대소문자를 구분하지 않는다. 현재 배열에 수식키 없는 대응 키가
없는 문자는 단축키를 조용히 무시하지 않고 이름 있는 키를 사용하도록 오류를 반환한다.
이 배열별 동작은 Windows에서 추가 확인해야 한다.

## C. 발견된 문제의 적용 결과

1. **파이프:** ProcessTools의 launch probe를 이름 열거로 변경했다. IpcServer의
   WaitForConnectionAsync를 연결별 try 안으로 옮기고 종료 취소를 정상 처리한다.
2. **주 창:** ProcessWindows는 Dispatcher에서 WPF MainWindow의 HwndSource를 우선한다.
   대체 창 선택에서도 제목 없는 장식 창을 제외한다.
3. **이벤트:** EventWatcher의 잡음 목록에 CanExecute를 추가했다.
4. **스크롤:** ScrollAction은 요청한 축의 범위가 없으면 높이·너비 0을 설명하는 오류를 반환한다.
   -1로 지정한 축은 그대로 둔다.
5. **세션 없음:** FindTools와 CaptureTools가 원인을 포함한 Error 응답을 각 도구의 결과 형태로 반환한다.
6. **문서:** README의 select·scroll 인자를 실제 이름으로 정정했다. assert는 이미 올바르므로 유지했다.

README, TOOLS_SPECIFICATION, 서버 지침, 도구 설명, 실제 스킬 경로인
`skills/xapper-mcp/SKILL.md`에 실행 방식 질문과 격리 제약을 반영했다.
스킬 수정 전후 별도 에이전트의 참고 문서 검색 시험에서, 수정 전 누락됐던 질문·격리 정책·
가운데 클릭 동작을 수정 후 정확히 찾았다.

## 검증 결과

| 검사 | 결과 |
| --- | --- |
| 전체 Debug 솔루션 교차 빌드 | 성공, 경고 **0**, 오류 **0**. |
| Inspector·Protocol 대상 | net462 및 net6/7/8/9.0-windows 모두 빌드. |
| TestApp 대상 | net48 및 net9.0-windows 모두 빌드. |
| 전체 xUnit 실행 시도 | Testhost가 Microsoft.WindowsDesktop.App 9.0을 찾지 못해 실행 전 중단. 실행된 테스트 **0개**. 통과한 테스트 없음. |
| Windows 실제 앱·DevExpress·잠금 중 검증 | 미실행. 현재 호스트는 Linux. |
| 독립 코드 리뷰 | 동시 세션 정리·격리 정책 원자성·직접 클릭 감시 문제를 발견해 보완하고 회귀 시험 추가. 배열 의존 테스트도 수정. |
| diff 공백 검사 | 통과. |

추가 회귀 테스트는 환경 값 판정, 환경 블록, 고유 이름, 누락 실행 파일, Unicode·공백 경로,
Job 자식 종료, 세션 detach·전체 dispose, Framework launch, 취소·빌드 거절,
서버 강제 종료, 격리 작업 거절, 스크립트 실패 캡처, 가운데 클릭·포커스 확정,
입력 필터·포커스 이동·시스템 키·키 눌림 해제, 세션 전환 경쟁, 파이프 재연결,
주 창 선택, 이벤트 잡음, 빈 스크롤 범위, 세션 없는 호출을 다룬다.
이 테스트들은 컴파일했으며 Windows에서의 통과를 확인한 것은 아니다.

결정적으로 재현하는 시험이 아직 없는 경로: Job 배치·ResumeThread 실패,
suspended 생성과 재개 사이 종료, 클라이언트 dispose 자체의 예외.
해당 정리 코드를 검토했지만 런타임 검증 증거는 없다.

Windows에서 다음 명령으로 전체 시험을 실행하고, 실제 앱 acceptance를 이어서 확인해야 한다.
net48 launch 시험에는 .NET Framework 4.x도 필요하다.

```powershell
dotnet build Xapper.slnx
dotnet test tests/Xapper.Tests/Xapper.Tests.csproj --no-build --logger "trx;LogFileName=isolated-input-windows.trx"
```

실제 앱 acceptance: 격리 launch, 합성 클릭·더블클릭·우클릭·휠, 콤보·메뉴·모달 및
Win32 대화상자 응답, render 캡처, screen/front/record 거절, 사람 Desktop의 해당 앱 창 0개,
detach와 서버 종료 후 프로세스·Desktop 제거, 잠금 상태에서 같은 동작 확인.

## 실행 중 판단과 한계

- 현재 checkout이 이미 격리되어 있어 추가 worktree를 만들지 않았다.
- 클라우드 스킬의 보조 스크립트·리뷰 템플릿이 파일로 제공되지 않아,
  동일한 작업 기록과 명시적 리뷰 범위로 대체했다. 제품 동작에는 영향이 없다.
- Windows 런타임 검증을 Linux 빌드로 대신 통과 처리하지 않았다. 남은 위험은
  실제 Windows 동작과 테스트 자체의 미검증이다.
- 동시 MCP 호출에서 소유권과 정책이 어긋나지 않도록 세션을 원자적으로 등록·조회한다.
  기존의 개별 딕셔너리 잠금만으로는 복합 조회의 일관성을 보장하지 못했다.
- 키보드 배열에 따른 시험 불안정은 지원 환경의 검증을 방해하므로 수정 대상에 포함했다.
  네이티브 실패를 강제로 주입하는 추가 프레임워크는 도입하지 않았다.
