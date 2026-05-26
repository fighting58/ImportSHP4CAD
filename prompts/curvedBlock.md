# AutoCAD C# 곡선 가구(街區) 경계 폴리라인 생성 플러그인 개발을 위한 완벽한 프롬프트

이 문서는 AutoCAD .NET API(C#) 환경에서 **곡선 가구(Calculated Curve Block) 경계 폴리라인 자동 생성 기능**을 완벽하게 구현하기 위한 상세 사양 및 프롬프트 가이드입니다. 

만약 이 기능을 AI 에이전트나 개발자에게 처음부터 개발하도록 지시해야 한다면, 아래 프롬프트를 복사하여 그대로 전달하시면 기하학적 예외와 AutoCAD API의 누수 없이 단 한 번에 최상의 코드를 산출할 수 있습니다.

---

## 🚀 AI 코딩 에이전트용 복사 가능 프롬프트 템플릿

```markdown
# [Task] AutoCAD .NET C# 곡선 가구(街區) 경계 폴리라인 생성 명령어 개발

## 1. 역할 및 목표
당신은 AutoCAD .NET Framework API(C#) 개발 전문가입니다.
사용자로부터 단일 호(Arc)를 입력받고, 도로폭, 허용 중앙종거, 계산방식(Simple/Refine)의 파라미터를 입력받아 가구(Block)의 안쪽 경계용 폴리라인(Yellow)과 바깥쪽 경계용 폴리라인(Blue)을 수학적으로 계산하여 도면 영역에 드로잉하는 AutoCAD 플러그인 명령어를 구현하세요.
이 명령어의 단축 실행 키는 `GAGU`로 정의합니다.

---

## 2. 세부 기하학적 계산 방식 및 수학 공식 (수식 준수 필수)

### [기본 공통 제원 계산]
1. 입력받은 `Arc` 객체에서 반지름($R$), 중심 좌표($Center$), 시작 각도($startAngle$, 라디안), 끝 각도($endAngle$, 라디안)를 추출합니다.
2. 호 전체 사이각 $arcAngle = endAngle - startAngle$을 구합니다. 만약 $arcAngle < 0$ 인 경우 $2\pi$를 더해 반시계 방향 회전 각도를 정규화합니다.
3. 주어진 허용 중앙종거($m$)와 반지름($R$)의 수학적 삼각비 관계를 통해 세분화 단위 편차각 $\theta$를 계산합니다:
   $$\cos\left(\frac{\theta}{2}\right) = 1.0 - \frac{m}{R}$$
   $$\theta = 2.0 \times \arccos\left(\text{clamp}\left(1.0 - \frac{m}{R}, -1.0, 1.0\right)\right)$$
   *(주의: Math.Acos 함수의 도메인 오류 방지를 위해 아코사인 인자값은 반드시 [-1.0, 1.0] 범위로 Math.Max/Min 클램핑 처리를 해야 합니다.)*
4. 호 전체 각도($arcAngle$)를 덮기 위해 필요한 등분 개수 $n$을 산출합니다:
   $$n = \text{ceiling}\left(\frac{arcAngle}{\theta}\right)$$
   *(만약 계산 결과 $n \le 0$인 경우 기본적으로 1로 보정합니다.)*
5. 실제 생성될 폴리라인의 등분할 정밀 각도 $\theta'$를 계산합니다:
   $$\theta' = \frac{arcAngle}{n}$$

---

### [계산 모드별 정점(Vertex) 좌표 산출 구조]

#### 1) SIMPLE 계산 방식
* **안쪽 폴리라인 (PolyIn)**: $n + 1$ 개의 정점으로 이루어지며, 기존 호의 등분할 원호 상의 좌표입니다.
  $$PointIn_i = Center + R \times (\cos(startAngle + i\theta'), \sin(startAngle + i\theta')) \quad (i = 0 \dots n)$$
* **바깥쪽 폴리라인 (PolyOut)**: 안쪽 폴리라인의 각 정점에서 정확히 도로폭($W$) 만큼 평행 오프셋된 $n + 1$ 개의 동심원 상 정점입니다.
  $$PointOut_i = Center + (R + W) \times (\cos(startAngle + i\theta'), \sin(startAngle + i\theta')) \quad (i = 0 \dots n)$$

#### 2) REFINE 계산 방식 (외선장 오프셋 모델)
안쪽 폴리라인 좌표는 **Simple 방식**과 동일하게 원호의 $n+1$개 점을 그대로 사용하지만, 바깥쪽 폴리라인은 가구 모퉁이의 기하학적 매끄러움을 극대화하기 위해 **외선장 반지름**($R_{refine}$)과 **하프 오프셋 각도(Half Offset Angle)**를 적용한 $n+2$개의 정점으로 정교하게 계산합니다.

* **바깥쪽 폴리라인 (PolyOut - $n+2$개 정점)**:
  * **첫 번째 정점 ($i = 0$)**: 시작 각도 기점의 도로폭 오프셋 좌표
    $$PointOut_0 = Center + (R + W) \times (\cos(startAngle), \sin(startAngle))$$
  * **중간 정점들 ($i = 1 \dots n$)**: 등분 간격의 중간 각도($i - 0.5$) 오프셋 방향 및 코사인 비율로 스케일 업된 외선장 반지름 적용
    $$R_{refine} = \frac{R + W}{\cos\left(\frac{\theta'}{2}\right)}$$
    $$PointOut_i = Center + R_{refine} \times \left(\cos\left(startAngle + (i - 0.5)\theta'\right), \sin\left(startAngle + (i - 0.5)\theta'\right)\right)$$
  * **마지막 정점 ($i = n + 1$)**: 끝 각도 기점의 도로폭 오프셋 좌표
    $$PointOut_{n+1} = Center + (R + W) \times (\cos(endAngle), \sin(endAngle))$$

---

## 3. 사용자 인터페이스 (Editor Prompts) 및 입력값 지속성
* AutoCAD Editor API의 `GetEntity`, `GetDouble`, `GetKeywords`를 사용하여 순차적으로 제어값을 입력받습니다.
  1. **호 선택**: `Arc` 타입 객체만 엄격히 선택 제한
  2. **도로폭(Road Width)**: 실수를 입력받음
  3. **중앙종거(Jonggeo)**: 실수를 입력받음
  4. **계산방식(Calc Mode)**: 키워드 `Simple` 또는 `Refine` 선택 제공
* **세션 유지 기능**: 다음번 명령어 실행 시에도 사용자가 이전 실행 때 입력했던 값(도로폭, 중앙종거, 계산방식)이 프롬프트의 기본값(Default)으로 계승되어 편리하게 나타나도록 클래스의 `static` 필드를 활용해 상태값을 보존해야 합니다.

---

## 4. 데이터베이스 및 엔티티 생성 요구사항
* 데이터베이스 트랜잭션(`Transaction`)을 열어 작업을 처리하고 예외 시 자동으로 Abort되도록 구성하세요.
* 도면 내 엔티티 추가 시의 세부 명세:
  * 두 폴리라인의 레이어(`Layer`)는 원본 호(Arc)가 속한 레이어와 동일하게 설정합니다.
  * 닫히지 않은 폴리라인(`Closed = false`) 구조로 렌더링합니다.
  * **안쪽 폴리라인**: 노란색 (AutoCAD ColorIndex = 2 / Yellow)
  * **바깥쪽 폴리라인**: 파란색 (AutoCAD ColorIndex = 5 / Blue)
* 성능 최적화를 위해 불필요한 임시 Arc 엔티티 생성 및 삭제 기법 대신, 순수 수학 삼각함수 공식만으로 실시간 Point 연산을 수행해야 합니다.
```

---

## 💡 이 프롬프트를 통해 기대되는 구현 성과
위 프롬프트는 아래와 같은 복잡한 AutoCAD .NET 개발의 우수 구현 패턴을 자연스럽게 내포하고 있습니다:
* **안전한 도메인 수학**: `Math.Acos` 진입 전의 클램핑 안전장치가 포함되어 `NaN` 오류를 사전에 철저히 방지합니다.
* **사용자 편의성 향상**: `static` 필드를 활용한 세션 영속성이 명시되어, 연속 작업 시 동일한 파라미터가 자동으로 저장/복원됩니다.
* **외선장 모델 완벽 매칭**: `Refine` 모델의 $\cos(\theta'/2)$ 역비율 스케일링 계산 수식이 포함되어, 복잡한 곡선 도로 설계 기하 규격에 단 일말의 오차도 없이 일치하게 정점을 플로팅합니다.
