## 목표
- C#을 이용한 AutoCAD 응용프로그램 작성
- 세부 구체적인 기능은 대화를 통해 재설정
- AutoCAD 2013과 완벽한 호환


## System Environment
- Windows11
- AutoCAD 2013


## 빌드 규칙

현재 프로젝트 `ShpCadImporter`는 AutoCAD 2013 환경과의 원활한 연동 및 개발 생산성을 보장하기 위해 다음과 같은 특화된 빌드 규칙을 준수합니다.
빌드는 사용자로부터 별도의 요구가 있을 때 수행합니다. 사용자의 요구가 없다면 빌드하지 않습니다.

### 1. 대상 프레임워크 및 호환성
* **Target Framework**: .NET Framework 4.5 (AutoCAD 2013 x64 실행용 .NET API 사양 준수)
* **어셈블리 참조 도구**: 개발 시스템에 별도의 .NET Framework 4.5 타겟 팩이 없더라도 정상적으로 빌드될 수 있도록 NuGet의 `Microsoft.NETFramework.ReferenceAssemblies.net45` 패키지를 통해 컴파일 플랫폼 어셈블리를 매핑합니다.

### 2. AutoCAD 프로세스 파일 잠금(Lock) 우회 규칙
* **문제 배경**: AutoCAD가 켜져 있는 상태에서 `NETLOAD`를 통해 한 번 로드된 DLL 파일은 AutoCAD 프로세스가 해당 파일을 메모리에 독점 점유하므로 덮어쓰기 재빌드가 불가능합니다.
* **우회 규칙**: 이를 회피하기 위해 빌드 시 출력 디렉토리(OutputPath)명을 순차적으로 변경하여 동적 빌드를 수행합니다.
  * 예: `Debug_v1` ➔ `Debug_v2` ➔ `Debug_v3` ➔ ... ➔ `Debug_v6`
* **빌드 실행 CLI 명령어 (PowerShell)**:
  ```powershell
  # Cwd: ShpCadImporter 프로젝트 폴더
  & "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe" ShpCadImporter.csproj /t:Rebuild /p:Configuration=Debug /p:Platform=x64 /p:OutputPath=bin\x64\Debug_v[버전]\
  ```
  *(참고: AutoCAD가 작동 중일 때 새 빌드를 실행하는 경우, 버전 번호 `[버전]`을 1씩 올려가며 지정하여 빌드합니다.)*

### 3. 종속성 격리 배포 구조 (`libs` 폴더 분리)
* **규칙 내용**: 메인 빌드 출력 폴더의 루트가 수많은 서드파티 DLL 어셈블리들로 혼잡해지는 것을 방지하기 위해, 외부 라이브러리 및 데이터베이스(MDB)를 하위 `libs\` 폴더 내에 일관되게 격리합니다.
* **프로젝트 파일 설정 (.csproj)**:
  * 서드파티 어셈블리 참조의 `Private` (로컬 복사) 속성을 `False`로 지정하여 출력 폴더 루트에 자동 배포되는 것을 막습니다.
  * 소스 디렉토리 하위의 종속 파일들을 `<Content>` 구성 요소로 등록하고 `<Link>libs\%(Filename)%(Extension)</Link>` 지정을 통해 빌드 시 출력 폴더의 `libs\` 내부에만 선택적으로 배치되도록 유도합니다.
* **런타임 동적 어셈블리 바인딩**:
  * 외부 참조 파일들이 `libs\` 폴더 내로 격리 배포되더라도 AutoCAD가 실행 중에 이를 정상적으로 바인딩할 수 있도록, `PluginInitializer.cs`에서 `AppDomain.CurrentDomain.AssemblyResolve` 이벤트를 구독하여 `[실행경로]\libs\` 하위에서 종속 어셈블리를 동적으로 탐색하고 바인딩하는 경로 해석(Resolver) 로직을 갖춥니다.
