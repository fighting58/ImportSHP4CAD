# AutoCAD 2013 호환 SHP Import Plugin 개발 계획서

## 프로젝트 개요

본 프로젝트는 AutoCAD 2013 환경에서 안정적으로 동작하는 SHP(Shapefile) Import Plugin을 개발하는 것을 목표로 한다.

플러그인은 다음 기능을 수행한다.

- MultiPolygon SHP 읽기
- LWPOLYLINE 생성
- Polygon 내부 안전 Text 생성
- Hole 포함 Polygon 처리
- 속성별 레이어 자동 생성
- AutoCAD 2013 안정 호환

---

# 1. 핵심 개발 목표

본 프로젝트의 가장 중요한 목표는 다음과 같다.

```text
AutoCAD 2013에서 안정적으로 동작
```

이를 위해 다음 원칙을 적용한다.

- 최신 .NET 기능 사용 금지
- 최신 NuGet 의존 최소화
- 외부 DLL 최소화
- .NET Framework 4.5 기반 개발
- AutoCAD 2013 API 기준 개발

---

# 2. 개발 환경

## 2.1 개발 언어

C#

---

## 2.2 .NET Framework

```text
.NET Framework 4.5
```

선정 이유:

- AutoCAD 2013 호환성 확보
- 최신 AutoCAD와도 대부분 호환 가능
- 외부 라이브러리 사용 가능 범위 확보

---

## 2.3 C# 언어 버전

```text
C# 7.3 이하
```

권장 이유:

- AutoCAD 2013 환경 안정성
- 최신 문법 의존 방지

금지 권장 기능:

- record
- nullable reference
- switch expression
- init property
- top-level statement

---

## 2.4 빌드 플랫폼

필수:

```text
x64
```

절대 금지:

```text
AnyCPU
x86
```

이유:

- AutoCAD 2013은 x64 전용 프로세스
- AnyCPU는 Prefer32Bit 기본값으로 인해 32비트 로드 시도 → 충돌
- x86 DLL은 x64 AutoCAD 프로세스에 로드 불가

csproj 필수 설정:

```xml
<PropertyGroup>
  <PlatformTarget>x64</PlatformTarget>
  <Prefer32Bit>false</Prefer32Bit>
</PropertyGroup>
```

---

## 2.5 개발 IDE

권장:

- Visual Studio 2019
- Visual Studio 2022

빌드 구성 주의:

- 솔루션 구성 관리자에서 플랫폼을 반드시 x64로 설정
- Debug/Release 모두 x64 확인

---

# 3. AutoCAD API 기준

## 3.1 대상 AutoCAD

주 대상:

```text
AutoCAD 2013 x64
```

---

## 3.2 참조 DLL

반드시 AutoCAD 2013 x64 설치 경로의 DLL 사용.

참조 대상:

```text
AcDbMgd.dll
AcMgd.dll
```

x64 설치 경로:

```text
C:\Program Files\Autodesk\AutoCAD 2013\
```

주의:

- 반드시 Program Files (x64) 경로 사용
- Program Files (x86) 경로의 DLL 사용 금지
- x64 AutoCAD에서 제공하는 Managed DLL은 MSIL이므로 x64 프로세스에서 정상 로드됨

---

## 3.3 DLL 설정

Visual Studio 참조 설정:

| 항목 | 값 | 이유 |
|---|---|---|
| Copy Local | False | AutoCAD 런타임에서 자체 로드 |
| Specific Version | False | 버전 간 호환성 확보 |

csproj 참조 예시:

```xml
<Reference Include="AcDbMgd">
  <HintPath>C:\Program Files\Autodesk\AutoCAD 2013\AcDbMgd.dll</HintPath>
  <Private>False</Private>
  <SpecificVersion>False</SpecificVersion>
</Reference>
<Reference Include="AcMgd">
  <HintPath>C:\Program Files\Autodesk\AutoCAD 2013\AcMgd.dll</HintPath>
  <Private>False</Private>
  <SpecificVersion>False</SpecificVersion>
</Reference>
```

---

# 4. SHP 데이터 구조

## 4.1 Geometry 타입

입력 데이터:

```text
MultiPolygon Only
```

특징:

- Concave Polygon 존재
- Hole 존재 가능
- MultiPolygon 가능
- Point 없음
- LineString 없음

---

## 4.2 속성 필드

| 필드명 | 설명 |
|---|---|
| MNUM | 관리번호 |
| NTFDATE | 날짜 |
| ALIAS | 별칭 |

---

# 5. Geometry 처리 정책

## 5.1 MultiPolygon 처리

MultiPolygon은 반드시 Polygon 단위로 분해한다.

예시:

```text
MultiPolygon
 ├─ Polygon A
 ├─ Polygon B
 └─ Polygon C
```

처리:

```text
Polygon A 처리
Polygon B 처리
Polygon C 처리
```

---

## 5.2 좌표 처리

좌표계(PRJ)는 사용하지 않는다.

정책:

```text
SHP 좌표 = AutoCAD 좌표
```

즉:

- 좌표변환 없음
- EPSG 처리 없음
- Projection 없음
- 단위변환 없음

---

## 5.3 Polygon 유효성 검사

수행하지 않음.

즉:

- polygon.IsValid 사용 안 함
- Buffer(0) 보정 안 함

사유:

- 원본 유지
- 성능 단순화

---

# 6. Hole 처리 정책

Hole은 별도 레이어를 생성하지 않는다.

처리 방식:

```text
ExteriorRing + InteriorRing 모두 동일 레이어
```

레이어:

```text
SHP_BOUNDARY
```

---

# 7. CAD Entity 생성 정책

## 7.1 Boundary 생성

생성 객체:

```text
LWPOLYLINE
```

설정:

| 속성 | 값 |
|---|---|
| Closed | true |
| Layer | SHP_BOUNDARY |

---

## 7.2 Hole 생성

Hole 역시:

```text
LWPOLYLINE
```

으로 생성.

레이어:

```text
SHP_BOUNDARY
```

---

# 8. Text 생성 정책

## 8.1 생성 레이어

| Layer | 내용 |
|---|---|
| MNUM_DATA | MNUM 값 |
| NTFDATE_DATA | NTFDATE 값 |
| ALIAS_DATA | ALIAS 값 |

---

## 8.2 Text 객체 종류

사용 객체:

```text
DBText
```

선정 이유:

- AutoCAD 2013 안정성
- 속도 우수
- 메모리 사용량 적음

---

## 8.3 Text 크기

고정:

```text
0.5
```

---

## 8.4 Text 정렬

정렬 방식:

```text
MiddleCenter
```

---

## 8.5 AutoCAD 2013 주의사항

DBText 정렬 시:

```text
SetDatabaseDefaults()
```

이후 Alignment 설정 필요.

권장 순서:

1. SetDatabaseDefaults()
2. Position 설정
3. Height 설정
4. HorizontalMode 설정
5. VerticalMode 설정
6. AlignmentPoint 설정

---

# 9. Text 위치 알고리즘

## 9.1 핵심 목표

반드시:

```text
텍스트 기준점이 Polygon 내부
```

보장.

---

## 9.2 1차 알고리즘

사용:

```text
Polylabel
```

목적:

- 가장 넓은 내부공간 탐색
- Hole 회피
- Concave Polygon 대응

---

## 9.3 2차 Fallback

Polylabel 실패 시:

```text
PointOnSurface
```

사용.

---

## 9.4 알고리즘 흐름

```text
Polygon
   ↓
Polylabel
   ↓
성공 → 사용
실패 → PointOnSurface
```

---

# 10. 외부 라이브러리 정책

AutoCAD 2013 안정성을 위해 외부 DLL 최소화.

---

# 10.1 NetTopologySuite

권장:

```text
구버전 사용
```

권장 버전:

```text
1.x 또는 2.x 초기버전
```

이유:

- .NET Framework 4.5 호환
- AutoCAD 2013 충돌 최소화

---

# 10.2 Polylabel

외부 NuGet 사용 대신:

```text
소스코드 직접 포함
```

권장.

이유:

- DLL 충돌 방지
- AutoCAD 2013 안정성 향상

---

# 10.3 DLL 최소화 정책

중요 원칙:

```text
외부 DLL 최소화
```

사유:

- AutoCAD 2013 DLL 충돌 빈번
- Assembly Resolve 문제 방지

---

# 11. 프로젝트 구조

권장 구조:

```text
ShpCadImporter.sln

 ├── Core
 │    ├── Geometry
 │    ├── Labeling
 │    ├── SHP
 │    └── CAD
 │
 └── AutoCADPlugin
```

---

# 12. 모듈 설계

## 12.1 SHP Reader

역할:

- SHP 읽기
- DBF 읽기
- MultiPolygon 반환

---

## 12.2 Geometry Processor

역할:

- MultiPolygon 분해
- Polygon 추출
- Ring 추출

---

## 12.3 Label Engine

역할:

- Polylabel 계산
- PointOnSurface fallback

---

## 12.4 Layer Manager

생성 대상:

```text
SHP_BOUNDARY
MNUM_DATA
NTFDATE_DATA
ALIAS_DATA
```

---

## 12.5 CAD Entity Builder

역할:

- LWPOLYLINE 생성
- DBText 생성
- Entity 추가

---

# 13. 처리 흐름

전체 실행 순서:

```text
1. IMPORT_SHP 실행
2. SHP 파일 선택
3. Layer 생성
4. SHP Read
5. MultiPolygon 분해
6. Polygon 처리
7. ExteriorRing 생성
8. InteriorRing 생성
9. Polylabel 계산
10. 실패 시 PointOnSurface
11. MNUM Text 생성
12. NTFDATE Text 생성
13. ALIAS Text 생성
14. Commit
```

---

# 14. 성능 전략

## 14.1 Transaction

권장:

```text
Single Transaction
```

---

## 14.2 Regen 최소화

작업 중 Regen 최소화.

---

## 14.3 Layer 캐싱

Layer 검색 반복 최소화.

---

# 15. AutoCAD 2013 x64 배포 전략

## 15.1 빌드 플랫폼

필수:

```text
x64
```

금지:

```text
AnyCPU (Prefer32Bit 문제로 충돌 발생)
x86 (x64 프로세스 로드 불가)
```

---

## 15.2 csproj 필수 설정

```xml
<Project ToolsVersion="12.0" DefaultTargets="Build">
  <PropertyGroup>
    <TargetFrameworkVersion>v4.5</TargetFrameworkVersion>
    <PlatformTarget>x64</PlatformTarget>
    <Prefer32Bit>false</Prefer32Bit>
    <LangVersion>7.3</LangVersion>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
```

---

## 15.3 출력 파일

배포 대상:

```text
ShpCadImporter.dll
```

---

## 15.4 외부 DLL x64 호환 확인

모든 외부 DLL은 x64 또는 AnyCPU(MSIL) 빌드여야 한다.

검증 방법:

```text
corflags.exe ShpCadImporter.dll
```

정상 결과:

| 항목 | 값 |
|---|---|
| PE | PE32+ (x64) 또는 PE32 (MSIL) |
| 32BITREQ | 0 |
| 32BITPREF | 0 |

32BITREQ=1 또는 32BITPREF=1이면 x64 AutoCAD에서 로드 실패.

---

## 15.5 외부 DLL 배포 위치

NetTopologySuite 등 외부 DLL 배치:

```text
Plugin.dll과 동일 폴더
```

또는 Assembly Resolve 핸들러 구현:

```csharp
// Plugin 초기화 시 등록
AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
{
    string folderPath = Path.GetDirectoryName(
        Assembly.GetExecutingAssembly().Location);
    string assemblyPath = Path.Combine(
        folderPath, new AssemblyName(args.Name).Name + ".dll");
    if (File.Exists(assemblyPath))
        return Assembly.LoadFrom(assemblyPath);
    return null;
};
```

이유:

- AutoCAD x64는 자체 Assembly 탐색 경로를 사용
- Plugin 폴더의 DLL을 자동 탐색하지 않음
- AssemblyResolve 미등록 시 FileNotFoundException 발생

---

## 15.6 로드 방법

AutoCAD 명령:

```text
APPLOAD
```

이후:

```text
IMPORT_SHP
```

실행.

---

## 15.7 x64 빌드 검증 체크리스트

배포 전 반드시 확인:

| 항목 | 확인 |
|---|---|
| PlatformTarget = x64 | ☐ |
| Prefer32Bit = false | ☐ |
| TargetFramework = v4.5 | ☐ |
| LangVersion = 7.3 | ☐ |
| AcDbMgd.dll Copy Local = False | ☐ |
| AcMgd.dll Copy Local = False | ☐ |
| 외부 DLL corflags 32BITREQ = 0 | ☐ |
| AssemblyResolve 핸들러 등록 | ☐ |
| AutoCAD 2013 x64에서 APPLOAD 테스트 | ☐ |

---

# 16. 디버깅 전략

Visual Studio에서:

```text
acad.exe (x64) Attach
```

방식 사용 권장.

디버거 설정:

| 항목 | 값 |
|---|---|
| Start External Program | C:\Program Files\Autodesk\AutoCAD 2013\acad.exe |
| Debugger Type | Managed (v4.0) |

주의:

- x64 acad.exe에 Attach 시 Mixed Mode 디버깅은 x64만 지원
- Native 디버깅이 필요하면 별도 x64 디버거 사용

---

# 17. 예외 처리 정책

## Empty Geometry

처리:

```text
Skip
```

---

## Null Attribute

처리:

```text
빈 문자열
```

---

## Polylabel 실패

처리:

```text
PointOnSurface fallback
```

---

# 18. 구현 우선순위

## 1단계

```text
AutoCAD 2013 Plugin 생성
```

---

## 2단계

```text
SHP Read
```

---

## 3단계

```text
MultiPolygon 분해
```

---

## 4단계

```text
LWPOLYLINE 생성
```

---

## 5단계

```text
Polylabel 구현
```

---

## 6단계

```text
DBText 생성
```

---

# 19. 최종 목표

최종 시스템은 다음을 수행한다.

```text
MultiPolygon SHP
    ↓
AutoCAD 2013 Import
    ↓
LWPOLYLINE 생성
    ↓
Polygon 내부 안전 Label 생성
    ↓
속성별 Layer 자동 구성
```

이를 통해 AutoCAD 2013 환경에서도 안정적으로 동작하는 GIS Polygon Import Plugin을 구축한다.
