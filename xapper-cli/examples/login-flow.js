// xapper run examples/login-flow.js

// 로그인 흐름을 자동화하는 Xapper 스크립트 예시
// 이 파일은 xapper run 명령으로 실행됩니다

// 1. 로그인 버튼 클릭
await xapper.click({ target: "name=LoginButton" });

// 2. 사용자명 입력
await xapper.type({ target: "name=UsernameBox", text: "admin" });

// 3. 비밀번호 입력
await xapper.type({ target: "name=PasswordBox", text: "password123" });

// 4. 확인 버튼 클릭
await xapper.click({ target: "name=OkButton" });

// 5. 로그인 완료 대기 및 검증
await xapper.assert({
  target: "name=WelcomeLabel",
  property: "Text",
  expected: "Welcome, admin!",
  timeout: 5000
});
