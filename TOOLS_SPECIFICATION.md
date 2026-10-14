# Xapper Tools Specification v0.1

모든 Xapper 도구의 정식 명세입니다. MCP 서버, CLI, 향후 다른 인터페이스는 이 명세를 따릅니다.

---

## 응답 형식

### 성공 (status = "ok")
```json
{
  "status": "ok",
  "result": "..." or {...}
}
```

### 실패 (status = "error")
```json
{
  "status": "error",
  "error": "설명 (첫 글자 대문자)"
}
```

### 타임아웃 (status = "timeout")
```json
{
  "status": "timeout",
  "error": "Tool name stayed ...: <원인>"
}
```

---

## 프로세스 관리

### xapper_launch
앱 시작. 훅을 주입한 후 자동 연결.

**인자:**
- `exePath` (string, 필수): 앱의 절대 경로
- `args` (string, 선택): 명령줄 인자
- `timeoutMs` (int, 선택, 기본 15000): 시작 대기 시간 (1000~300000)

**응답:**
```json
{
  "status": "ok",
  "result": "Started PID 1234."
}
```

**실패 원인:**
- `no file at <path>` — 파일 없음
- `could not start <name> (...)` — 시작 실패
- `exited with code X before the inspector came up` — 앱이 즉시 종료
- `timeout` — 훅 로드 시간 초과

---

### xapper_attach
실행 중인 프로세스에 훅 주입.

**인자:**
- `processId` (int, 필수): 프로세스 ID
- `timeoutMs` (int, 선택, 기본 15000): 주입 대기 시간

**응답:**
```json
{
  "status": "ok",
  "result": "Attached to <name> (PID 1234)."
}
```

**실패 원인:**
- `no process with ID X` — PID 없음
- `is not a WPF app` — WPF가 아님
- `already attached` — 이미 연결됨
- `timeout` — 훅 로드 시간 초과

---

### xapper_detach
현재 세션 분리 (앱 닫지 않음).

**인자:** 없음

**응답:**
```json
{
  "status": "ok",
  "result": "Detached from <name>."
}
```

---

### xapper_list_processes
실행 중인 WPF 프로세스 목록.

**인자:** 없음

**응답:**
```json
{
  "status": "ok",
  "result": [
    {
      "processId": 1234,
      "name": "MyApp",
      "title": "My Application - Document.txt"
    }
  ]
}
```

---

## UI 동작

### xapper_click
요소 클릭 (또는 화면 좌표).

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref 번호
- `x` (float, 선택): 상대 좌표 (0.0~1.0, 요소 기준)
- `y` (float, 선택): 상대 좌표
- `modifiers` (string, 선택): "Ctrl", "Shift", "Alt", "Ctrl+Shift" 등
- `timeout` (int, 선택, 기본 5000): 요소 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Clicked via <path>.<warning>"
}
```

`<path>`: `accessibility`, `button event`, `routed event`, `real mouse` 중 하나  
`<warning>`: 부연 설명 (예: "not foreground" 등) 또는 빈 문자열

**응답 (실패):**
```json
{
  "status": "timeout",
  "error": "xapper_click stayed hidden: element not found after 5000ms"
}
```

**실패 원인:**
- `stayed disabled` — 요소가 비활성화됨
- `stayed hidden` — 화면에 보이지 않음
- `was still loading` — UI가 정착하지 않음
- `cannot be resolved` — ref가 유효하지 않음
- `matches N elements` — 선택자가 모호함

---

### xapper_doubleclick
더블클릭.

**인자:** xapper_click과 동일

**응답:** xapper_click과 동일

---

### xapper_rightclick
우클릭.

**인자:** xapper_click과 동일

**응답:** xapper_click과 동일

---

### xapper_type
텍스트 입력.

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `text` (string, 필수): 입력할 텍스트
- `timeout` (int, 선택, 기본 5000): 요소 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Typed: <text>"
}
```

**실패 원인:** xapper_click과 동일 + "does not support text input"

---

### xapper_key
키 입력.

**인자:**
- `key` (string, 필수): "Enter", "Escape", "F1", "Ctrl+C", "Ctrl+Shift+S" 등
- `modifiers` (string, 선택): "Ctrl", "Shift", "Alt"
- `timeout` (int, 선택, 기본 0): 대기 시간 (0 = 즉시)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Sent: <key>"
}
```

---

### xapper_select
선택 (ComboBox, ListBox 등).

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `value` (string, 필수): 선택할 값 (텍스트)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Selected: <value>"
}
```

**실패 원인:** "does not support selection" 등

---

### xapper_toggle
토글 (CheckBox, ToggleButton 등).

**인자:**
- `target` (string|int, 필수)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Toggled to: <true|false>"
}
```

---

### xapper_expand
트리 노드 확장 (TreeViewItem 등).

**인자:**
- `target` (string|int, 필수)
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Expanded."
}
```

---

### xapper_scroll
스크롤.

**인자:**
- `target` (string|int, 필수): 스크롤바 또는 ItemsControl
- `direction` (string, 필수): "up", "down", "left", "right"
- `amount` (int, 선택, 기본 3): 스크롤 량
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Scrolled <direction> by <amount>"
}
```

---

### xapper_drag
드래그. 요소 기반 또는 화면 좌표 기반.

**인자:**
- `source` (string|int, 선택): 시작 요소
- `sourceX` (float, 선택): 시작 화면 좌표 X (픽셀)
- `sourceY` (float, 선택): 시작 화면 좌표 Y
- `target` (string|int, 선택): 끝 요소
- `targetX` (float, 선택): 끝 화면 좌표 X
- `targetY` (float, 선택): 끝 화면 좌표 Y
- `offsetX` (float, 선택): 상대 이동 X (source 기준)
- `offsetY` (float, 선택): 상대 이동 Y
- `modifiers` (string, 선택): "Ctrl" 등
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Dragged via <thumb|synthetic|real mouse>"
}
```

**필수 조합:**
- `source` XOR (`sourceX` + `sourceY`)
- `target` OR `offsetX/offsetY` OR (`targetX` + `targetY`)

---

## 읽기

### xapper_find
요소 검색.

**인자:**
- `target` (string, 필수): 선택자
- `timeout` (int, 선택, 기본 2000): 검색 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": [
    {
      "ref": 1,
      "type": "Button",
      "name": "OkButton",
      "text": "OK",
      "visible": true,
      "enabled": true,
      "bounds": {"x": 100, "y": 200, "width": 60, "height": 30}
    }
  ]
}
```

---

### xapper_snapshot
요소 내용 텍스트로.

**인자:**
- `ref` (int, 선택): 요소 ref (없으면 전체)
- `limit` (int, 선택, 기본 1000): 최대 줄 수

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "요소 내용\n여러 줄\n..."
}
```

---

### xapper_get_property
요소 속성 읽기.

**인자:**
- `target` (string|int, 필수): 선택자 또는 ref
- `property` (string, 필수): 속성명 (예: "Text", "IsChecked")
- `timeout` (int, 선택, 기본 5000)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "속성값"
}
```

---

### xapper_assert
값 검증.

**인자:**
- `target` (string|int, 필수)
- `property` (string, 필수)
- `expected` (string|number|boolean, 필수)
- `timeout` (int, 선택, 기본 5000): 조건 대기 시간

**응답 (성공):**
```json
{
  "status": "ok",
  "result": "Assertion passed: <property> == <expected>"
}
```

**응답 (실패):**
```json
{
  "status": "error",
  "error": "Assertion failed: Text == 'OK' (got 'Cancel')"
}
```

---

### xapper_screenshot
스크린샷 캡처.

**인자:**
- `ref` (int, 선택): 요소 ref (없으면 전체 데스크톱)
- `annotate` (boolean, 선택): true = 클릭 가능 요소에 숫자 표시
- `path` (string, 선택): 저장 경로 (없으면 메모리만)

**응답 (성공):**
```json
{
  "status": "ok",
  "result": {
    "width": 1920,
    "height": 1080,
    "imageBase64": "iVBORw0KGgoAAAANS...",
    "path": "C:\\results\\screenshot.png"
  }
}
```

---

### xapper_element_at
화면 좌표의 요소 찾기.

**인자:**
- `x` (int, 필수): 화면 좌표 X (픽셀)
- `y` (int, 필수): 화면 좌표 Y

**응답:**
```json
{
  "status": "ok",
  "result": {
    "ref": 5,
    "type": "Button",
    "name": "SaveButton",
    "text": "Save"
  }
}
```

---

### xapper_events
앱이 발생시킨 이벤트 목록.

**인자:**
- `count` (int, 선택, 기본 20): 반환할 최근 이벤트 수
- `sinceRef` (int, 선택): 이 ref 이후의 이벤트만

**응답:**
```json
{
  "status": "ok",
  "result": [
    {
      "type": "SelectionChanged",
      "source": "OrderGrid",
      "timestamp": "2026-10-14T10:30:45Z"
    },
    {
      "type": "TextChanged",
      "source": "SearchBox",
      "value": "test",
      "timestamp": "2026-10-14T10:30:46Z"
    }
  ]
}
```

---

### xapper_binding_errors
Inspector 가 들어간 뒤 앱이 낸 XAML 바인딩 오류(경로 없음, 소스 없음, 변환 실패). 이 오류는 요소에 남지 않아
`xapper_get_bindings` 로는 보이지 않고 WPF 바인딩 트레이스에만 나온다. 객체 해시만 다른 같은 오류(목록의 행마다
반복되는 오류)는 한 줄로 합쳐 횟수를 센다. 서로 다른 오류는 100건까지 담고, 넘친 것은 건수만 센다.

`xapper_launch` 는 첫 화면의 오류부터 잡는다. `xapper_attach` 는 붙은 뒤에 난 오류만 보므로, 그 전 화면은 다시 열어야 한다.

**인자:**
- `clear` (bool, 선택, 기본 false): 읽은 뒤 비운다. 다음 호출은 그 뒤에 난 오류만 보여 준다.

**응답 (텍스트):**
```
2 distinct binding errors since collection started 12 s ago, first seen first:
  x14  last 3021 ms ago  BindingExpression path error: 'Nmae' property not found on 'object' ''Person''. BindingExpression:Path=Nmae; DataItem='Person'; target element is 'TextBlock' (Name=''); target property is 'Text' (type 'String')
  x1  last 2988 ms ago  Cannot find source for binding with reference 'ElementName=Missing'. ...
```

비었을 때: `No binding errors since collection started 12 s ago. Errors raised before that were not seen.`

**실패 원인:**
- `No active session` — 연결된 앱 없음
- `Binding errors are not being collected in this process` — 이 기능이 없는 이전 Inspector 가 주입된 앱

---

## 스크립트 실행

### xapper_run
JavaScript 스크립트 실행.

**인자:**
- `script` (string, 선택): JavaScript 코드
- `scriptPath` (string, 선택): 스크립트 파일 경로
- `timeoutMs` (int, 선택, 기본 60000): 제한 시간 (1000~300000)
  - 시간 초과 시 스크립트는 강제 중단됨 (무한루프 포함)
  - Jint 엔진의 취소 토큰 + Task.WhenAny 이중 타임아웃으로 보장

`script` 또는 `scriptPath` 중 정확히 하나만 제공.

**응답 (성공):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "success",
    "duration": 1234,
    "returnValue": null,
    "log": ["Step 1", "Step 2"]
  }
}
```

**응답 (실패):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "fail",
    "duration": 500,
    "line": 10,
    "error": "Element not found",
    "trace": "at click (line 10)",
    "screenshot": "C:\\results\\failure.png"
  }
}
```

**응답 (타임아웃):**
```json
{
  "status": "ok",
  "result": {
    "outcome": "timeout",
    "duration": 60000,
    "error": "Script exceeded 60000ms"
  }
}
```

**스크립트 내 전역 객체:**
```javascript
xapper = {
  // 읽기
  find(selector) → [{ref, type, name, ...}],
  one(selector) → element (첫 번째),
  get(target, property) → value,
  assert(target, property, expected, {timeout}) → void,
  
  // 동작
  click(target, {x, y, modifiers, timeout}) → void,
  doubleClick(target, {x, y, modifiers, timeout}) → void,
  rightClick(target, {x, y, modifiers, timeout}) → void,
  type(target, text) → void,
  key(keys, {modifiers}) → void,
  select(target, value) → void,
  toggle(target) → void,
  expand(target) → void,
  scroll(target, direction, {amount}) → void,
  drag(source, {sourceX, sourceY, targetX, targetY, offsetX, offsetY, modifiers, timeout}) → void,
  
  // 창·대화상자 (모달을 띄운 click 은 실패하지 않고 돌아온다)
  windows({since}) → {now, open: [...], shown: [...]},
  messageBox(button, {timeout}) → string,
  fileDialog(path, {timeout}) → string,
  window(action, {hwnd}) → string,
  
  // 검증
  waitUntil(selector, {timeout}) → void,
  screenshot({path, annotate}) → void,
  events({count, sinceRef}) → [...],
  
  // 유틸
  sleep(ms) → void,
  log(...args) → void,
  fail(message) → void
};

notice = {
  show(message) → void,
  hide() → void
};
```

---

### xapper_batch
여러 도구를 한 번에 호출.

**인자:**
- `steps` (array, 필수): 도구 호출 배열

**각 step:**
```json
{
  "tool": "xapper_click",
  "target": "name=Button",
  "x": 0.5,
  "y": 0.5
}
```

**응답 (성공):**
```json
{
  "status": "ok",
  "result": [
    {
      "stepIndex": 0,
      "tool": "xapper_click",
      "status": "ok",
      "result": "Clicked via routed event"
    },
    {
      "stepIndex": 1,
      "tool": "xapper_type",
      "status": "ok",
      "result": "Typed: hello"
    }
  ]
}
```

실패한 step에서 배치 중단. 이후 step은 실행되지 않음.

---

## 화면 기록

### xapper_screen_learn
현재 화면을 기억.

**인자:**
- `name` (string, 필수): 화면 이름 (예: "login-screen")
- `notes` (string, 선택): 주의사항

**응답:**
```json
{
  "status": "ok",
  "result": "Learned screen 'login-screen' with 12 selectors"
}
```

---

### xapper_screen_recall
기억한 화면의 선택자 반환.

**인자:**
- `name` (string, 필수): 화면 이름

**응답:**
```json
{
  "status": "ok",
  "result": "name=UsernameBox    TextBox\nname=PasswordBox    PasswordBox\n..."
}
```

---

### xapper_screen_note
화면에 주의사항 추가.

**인자:**
- `name` (string, 필수): 화면 이름
- `note` (string, 필수): 추가할 주의사항
- `replace` (boolean, 선택): true = 기존 노트 교체, false = 추가

**응답:**
```json
{
  "status": "ok",
  "result": "Added note to 'login-screen'"
}
```

---

### xapper_screen_forget
기억한 화면 삭제.

**인자:**
- `name` (string, 필수)

**응답:**
```json
{
  "status": "ok",
  "result": "Forgot screen 'login-screen'"
}
```

---

## Win32 창·대화상자 도구

MessageBox 와 공용 열기·저장 대화상자는 Win32 창이라 스냅샷에 나오지 않고, 떠 있는 동안 앱은 다른 조작을 처리하지 않는다.

공통 규칙
- 연결된 앱 프로세스가 소유한 최상위 창만 다룬다. 다른 프로그램의 창은 찾지도 누르지도 않는다.
- 실패하면 응답 글이 `Error:` 로 시작하고 MCP `isError` 가 켜진다.
- 시각은 앱 프로세스의 UTC, ISO-8601 밀리초(`2026-10-14T05:12:03.123Z`)다.
- UI 스레드를 거치지 않으므로 모달이 떠 있어도 응답한다.

### xapper_list_windows
앱의 최상위 창과, 기준 시각 이후에 뜬 창의 기록.

**인자:**
- `since` (string, 선택): 이 시각 이후에 뜬 창만 `shown` 에 담는다. 앞선 응답의 `now` 를 넘긴다. 없으면 보관 중인 전부(최근 100개).

**응답 (JSON 한 덩어리):**
```json
{
  "now": "2026-10-14T22:56:39.783Z",
  "open": [
    { "hwnd": "0x10A26", "title": "XapperLive Confirm", "className": "#32770", "kind": "dialog", "modal": true,
      "owner": "0x209CC", "shownAt": "2026-10-14T22:56:39.501Z", "hiddenAt": null }
  ],
  "shown": [ /* 같은 모양. since 이후에 뜬 창, 뜬 순서. 닫힌 창은 hiddenAt 이 있다 */ ]
}
```
- `open`: 지금 보이는 창, Z 순서 앞부터. 감시 시작 전부터 열려 있던 창은 `shownAt` 이 null.
- `kind`: `dialog`(#32770) · `window`(제목 표시줄이 있는 창). 메뉴·툴팁·드롭다운은 모달이 아니면 담지 않는다.
- `modal`: 뜰 때 다른 창을 막았는지. #32770 은 언제나 true.

**NO_MODAL 판정:** 조작 전 응답의 `now` 를 `since` 로 넘겨 다시 부르고, `shown` 에 `"modal": true` 가 없으면 NO_MODAL.

**실패 원인:**
- `since "..." is not a time` — 시각 형식 오류

---

### xapper_message_box
앱의 메시지 상자를 기다려 버튼을 누르고 닫혔는지 확인한다. 버튼은 표시 글자가 아니라 표준 ID 로 누르므로 Windows 언어와 무관하다.

**인자:**
- `button` (string, 필수): `ok` `cancel` `yes` `no` `abort` `retry` `ignore` `close` `tryagain` `continue` (대소문자 무관)
- `timeout` (int, 선택, 기본 5000): 상자가 나타나기를 기다릴 시간(ms)

**응답:**
```
Answered "XapperLive Confirm" with yes. Message: "Save changes?"
```

**실패 원인:**
- `Unknown button "..."` — 목록에 없는 이름
- `No message box appeared within N ms.`
- `"<제목>" has no yes button. It has: ok, cancel.`
- `"<제목>" is still open after pressing yes.`

---

### xapper_file_dialog
앱의 열기·저장 대화상자를 기다려 파일 이름 칸에 경로를 넣고 확인한 뒤 닫혔는지 확인한다.

**인자:**
- `path` (string, 필수): 절대 경로
- `timeout` (int, 선택, 기본 5000)

**응답:**
```
Answered "<제목>" with C:\data\orders.csv
```

**실패 원인:**
- `"<path>" is not an absolute path.`
- `No open or save dialog appeared within N ms.`
- `"<제목>" opened "<후속 제목>": <본문> It is still open; answer it with xapper_message_box.` — 덮어쓰기 확인·"파일 없음" 같은 후속 상자. 그 상자는 열린 채로 둔다.
- `"<제목>" is still open after confirming <path>.`

---

### xapper_window
앱의 창 하나를 최대화·복원·최소화하거나 앞으로 가져온 뒤 실제 창 상태로 확인한다.

**인자:**
- `action` (string, 필수): `maximize` `restore` `minimize` `front`. `restore` 는 일반 크기로 되돌린다.
- `hwnd` (string, 선택): `xapper_list_windows` 의 핸들. 없으면 앱의 주 창.

**응답:**
```
Maximized "Xapper Test App" (0x209CC)
```

**실패 원인:**
- `Unknown action "..."`
- `hwnd "..." is not a window handle.` / `0x... is not a window of this app.`
- `Windows refused to bring "<제목>" to the front. ...` — 다른 앱이 입력을 쥐고 있을 때
- `"<제목>" (0x...) did not change within 2000 ms.`

---

### 모달을 띄운 클릭
`xapper_click`·`xapper_doubleclick`(과 스크립트의 `click`·`doubleClick`)이 앱의 모달을 띄우면 제한 시간을 채우지 않고 뜨는 즉시 돌아온다.

```
The click on ref=1 opened a modal dialog "XapperLive Confirm" (0x10A26). The app handles nothing else until it closes. Answer it with xapper_message_box, or xapper_file_dialog for an open or save dialog.
```
- MCP 도구: 성공(`isError` 꺼짐). `xapper_batch` 는 이 단계에서 멈춘다.
- 스크립트: 실패로 보지 않고 다음 줄로 진행한다. 이어서 `xapper.messageBox()` 로 답한다.

---

## 선택자 문법

```
"name=ButtonName"           — x:Name 속성
"id=SaveButton"             — x:Name (id는 별칭)
"text=Click Me"             — TextBlock, Button Content 등
"type=Button"               — 타입명
"text=로그인,type=Button"   — AND (쉼표)

ref 숫자                     — find 응답의 ref 직접 사용

element 객체                 — find/one 결과 직접 사용
```

---

## 응답 규칙

1. **성공:** `status: "ok"` + `result`
2. **실패:** `status: "error"` + `error` (자세한 설명)
3. **타임아웃:** `status: "timeout"` + `error` ("stayed <원인>")
4. **배치 중단:** 첫 실패 step에서 중단, 이후 step은 skip

---

## 버전 히스토리

### v0.1 (2026-10-14)
- 초안 작성
- 기존 도구 명세화
- Win32 도구는 향후 추가 예정
