# VsLikeDoking
---
해당위치: VsLikeDoking  
생성일: 2025-12-20  
tag:  
Link:  
---

비주얼스튜디오 윈도우 도커와 같은 기능을 제공하는 DLL

---


# 목차
1. [코드트리](#코드트리)
1. 사용법
1. 
1. 

---

# 코드트리
<a id="코드트리"></a>

┌ VsLikeDoking.sln  
├┬ Abstractions  // 외부 연결 계약(콘텐츠/팩토리/커맨드/퍼시스트)   
│├─ IDockCommand.cs  // 커맨드 계약(실행은 Core)  
│├─ IDockContent.cs  // 콘텐츠(Control/Title 등) 계약  
│├─ IDockContentFactory.cs  // 복원/생성 계약  
│└─ IDockPersistable.cs  // PersistKey 계약  
├┬ Core  // 운영 계층(설정/이벤트/레지스트리/매니저/진단)  
│├┬ Commands // 커맨드 실행/수렴(성능:coalescing/큐)  
││├─ BuiltInDockCommands.cs  


│└├┬─
