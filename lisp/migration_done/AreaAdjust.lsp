;;; AreaAdjust.lsp
;;; 지적 공차면적 기준 통합 면적 조정 도구 (이동형 & 고정형)
;;; AutoCAD 2013 & Windows 11 환경 최적화

(vl-load-com)

;; ==========================================
;; [1] 공통 유틸리티 및 수식 함수
;; ==========================================

;; 정점 추출
(defun util:get-vertices (ent / pts)
  (foreach x (entget ent) (if (= (car x) 10) (setq pts (cons (cdr x) pts))))
  (reverse pts)
)

;; CW 면적 계산 (양수 반환)
(defun util:get-signed-area (pts / area p1 p2)
  (setq area 0.0)
  (setq pts (append pts (list (car pts))))
  (while (and (setq p1 (car pts)) (setq p2 (cadr pts)))
    (setq area (+ area (* (- (car p2) (car p1)) (+ (cadr p2) (cadr p1)))))
    (setq pts (cdr pts))
  )
  (* 0.5 area)
)

;; 시계 방향 강제 정렬
(defun util:ensure-clockwise (pts)
  (if (< (util:get-signed-area pts) 0) (reverse pts) pts)
)

;; 공차 면적 공식: F = 0.026^2 * M * sqrt(S)
(defun util:calc-tolerance (s scale / m f)
  (setq m scale f (* (* 0.026 0.026) m (sqrt s)))
  (atof (rtos f 2 3))
)

;; 입력 파싱 및 목표 면적 산출
(defun util:parse-target (current-area input / prefix val scale reg-area tol-area target)
  (setq input (strcase (vl-string-trim " " input)) prefix (substr input 1 1))
  (cond
    ((= prefix "T") (setq target (atof (substr input 2))))
    ((and (= prefix "D") (/= (substr input 2 1) "-")) (setq target (+ current-area (atof (substr input 2)))))
    ((and (= prefix "D") (= (substr input 2 1) "-")) (setq target (- current-area (atof (substr input 3)))))
    ((= prefix "@") (setq reg-area (atof (substr input 2)) scale 6000))
    ((= prefix "#") (setq reg-area (atof (substr input 2)) scale 1000))
    ((numberp (read input)) (setq reg-area (atof input) scale 1200))
  )
  (if (and reg-area (not target))
    (progn
      (setq tol-area (util:calc-tolerance reg-area scale))
      (if (>= current-area reg-area) (setq target (+ reg-area tol-area -1.0)) (setq target (+ (- reg-area tol-area) 1.0)))
    )
  )
  target
)

;; 점 좌표 형식 검사 (최소 x,y 보유)
(defun util:point2d-p (p)
  (and (listp p) (numberp (car p)) (numberp (cadr p)))
)

;; 두 직선의 교차점
;; 두 직선의 교차점(무한 직선 기준: 선분 끝을 넘어 연장 교차 허용)
(defun util:intersect-lines (p1 p2 p3 p4 / ip)
  (setq ip (inters (list (car p1) (cadr p1) 0.0)
                   (list (car p2) (cadr p2) 0.0)
                   (list (car p3) (cadr p3) 0.0)
                   (list (car p4) (cadr p4) 0.0)
                   nil))
  (if (and ip (listp ip) (< (abs (car ip)) 1e10) (< (abs (cadr ip)) 1e10))
    (list (car ip) (cadr ip))
    nil
  )
)
;; 최접점 정점 인덱스
(defun util:get-nearest-vertex-idx (pt pts fuzz / idx min-dist res-idx i d)
  (setq i 0 min-dist 1e9 res-idx -1)
  (foreach v pts (setq d (distance pt v)) (if (< d min-dist) (setq min-dist d res-idx i)) (setq i (1+ i)))
  res-idx
)

;; 인덱스가 idx1과 idx2 사이에 있는지 확인
(defun util:is-between-indices (i idx1 idx2 n)
  (if (< idx1 idx2) (and (> i idx1) (< i idx2)) (or (> i idx1) (< i idx2)))
)

;; 루프 매크로 대용

;; ==========================================
;; [2-A] 엔진 1: 구간 이동 방식 (P1, P2 이동)
;; ==========================================

(defun fn:adjust-area-move (pts-list idx1 idx2 target-area /
                            pts n i p1 p2 p-prev p-next seg-len offset-dist
                            v-inward iter current-area
                            idx-prev idx-next d-val
                            final-seg head tail changed cnt-a seg-a seg-b
                            offset-lines p1-off p2-off line-prev line-first line-last line-next p-new j L1 L2
                            total-offset continue)
  (setq iter 0 current-area (util:get-signed-area pts-list) n (length pts-list) changed nil total-offset 0.0)
  (while (and (< iter 30) (> (abs (- target-area current-area)) 0.01))
    (setq seg-len 0.0 i idx1)
    (setq continue T)
    (while continue
      (setq p1 (nth i pts-list) p2 (nth (rem (1+ i) n) pts-list))
      (setq seg-len (+ seg-len (distance p1 p2)))
      (if (= (rem (1+ i) n) idx2) (setq continue nil) (setq i (rem (1+ i) n)))
    )

    (setq offset-dist (/ (- target-area current-area) seg-len -1.0))
    (setq total-offset (- total-offset offset-dist))
    (setq idx-prev (if (= idx1 0) (1- n) (1- idx1)) idx-next (rem (1+ idx2) n))
    (setq p-prev (nth idx-prev pts-list) p-next (nth idx-next pts-list))

    (setq offset-lines nil i idx1)
    (setq continue T)
    (while continue
      (setq p1 (nth i pts-list) p2 (nth (rem (1+ i) n) pts-list) d-val (max 1e-9 (distance p1 p2)))
      (setq v-inward (list (/ (- (cadr p2) (cadr p1)) d-val) (/ (- (car p1) (car p2)) d-val)))
      (setq p1-off (list (+ (car p1) (* offset-dist (car v-inward))) (+ (cadr p1) (* offset-dist (cadr v-inward)))))
      (setq p2-off (list (+ (car p2) (* offset-dist (car v-inward))) (+ (cadr p2) (* offset-dist (cadr v-inward)))))
      (setq offset-lines (append offset-lines (list (list p1-off p2-off))))
      (if (= (rem (1+ i) n) idx2) (setq continue nil) (setq i (rem (1+ i) n)))
    )

    (setq final-seg nil)
    
    (setq line-prev (list p-prev (nth idx1 pts-list)))
    (setq line-first (car offset-lines))
    (setq p-new (util:intersect-lines (car line-prev) (cadr line-prev) (car line-first) (cadr line-first)))
    (if (not p-new) (setq p-new (car line-first)))
    (setq final-seg (append final-seg (list p-new)))

    (setq j 0)
    (while (< j (1- (length offset-lines)))
      (setq L1 (nth j offset-lines) L2 (nth (1+ j) offset-lines))
      (setq p-new (util:intersect-lines (car L1) (cadr L1) (car L2) (cadr L2)))
      (if (not p-new) (setq p-new (cadr L1)))
      (setq final-seg (append final-seg (list p-new)))
      (setq j (1+ j))
    )

    (setq line-next (list (nth idx2 pts-list) p-next))
    (setq line-last (nth (1- (length offset-lines)) offset-lines))
    (setq p-new (util:intersect-lines (car line-last) (cadr line-last) (car line-next) (cadr line-next)))
    (if (not p-new) (setq p-new (cadr line-last)))
    (setq final-seg (append final-seg (list p-new)))

    ;; clockwise 구간 치환: idx1->idx2가 래핑되는 경우를 분리 처리
    (if (<= idx1 idx2)
      (progn
        (setq head nil i 0)
        (while (< i idx1) (setq head (append head (list (nth i pts-list)))) (setq i (1+ i)))
        (setq tail nil i (1+ idx2))
        (while (< i n) (setq tail (append tail (list (nth i pts-list)))) (setq i (1+ i)))
        (setq pts-list (append head final-seg tail) changed T)
      )
      (progn
        ;; 래핑 구간은 final-seg를 [idx1..n-1], [0..idx2]로 분리 후 원래 0번 시작 순서로 재조립
        (setq cnt-a (- n idx1)
              seg-a (vl-subseq-custom final-seg 0 cnt-a)
              seg-b (vl-subseq-custom final-seg cnt-a (length final-seg)))
        (setq tail nil i (1+ idx2))
        (while (< i idx1) (setq tail (append tail (list (nth i pts-list)))) (setq i (1+ i)))
        (setq pts-list (append seg-b tail seg-a) changed T)
      )
    )

    (setq current-area (util:get-signed-area pts-list) iter (1+ iter))
  )
  (if changed 
    (list pts-list total-offset)
    nil
  )
)

;; ==========================================
;; [2-B] 엔진 2: 고정점 방식 (P1, P2 유지)
;; ==========================================

(defun fn:adjust-area-fixed (orig-pts-list idx1 idx2 target-area /
                             n i current-area iter changed
                             sub-pts sub-len offset-dist offset-lines
                             v-inward d-val p1 p2 p1-off p2-off j L1 L2 p-new
                             final-seg head tail total-offset geom-offset
                             pts-list base-pts-list new-pts-list mid-pt cnt-a seg-a seg-b p-chord1 p-chord2 continue)
  (setq n (length orig-pts-list) iter 0 
        current-area (util:get-signed-area orig-pts-list)
        total-offset 0.0 geom-offset 0.0 changed nil)
  
  (setq pts-list orig-pts-list)

  ;; 1. 정점이 없으면 중간점 생성
  (if (= (rem (1+ idx1) n) idx2)
    (progn
      (setq p-chord1 (nth idx1 pts-list) p-chord2 (nth idx2 pts-list)
            mid-pt (list (* 0.5 (+ (car p-chord1) (car p-chord2))) (* 0.5 (+ (cadr p-chord1) (cadr p-chord2)))))
      (if (< idx1 idx2)
        (progn
          (setq head (vl-subseq-custom pts-list 0 (1+ idx1))
                tail (vl-subseq-custom pts-list (1+ idx1) n)
                pts-list (append head (list mid-pt) tail)
                idx2 (1+ idx2))
        )
        (progn
          (setq pts-list (append pts-list (list mid-pt)))
        )
      )
      (setq n (1+ n) changed T)
    )
  )

  (setq base-pts-list pts-list new-pts-list pts-list)

  (while (and (< iter 50) (> (abs (- target-area current-area)) 0.001))
    ;; 2. P1 ~ P2 구간의 점들(sub-pts)과 총 길이(sub-len) 추출
    (setq sub-pts nil sub-len 0.0 i idx1 continue T)
    (while continue
      (setq p1 (nth i base-pts-list))
      (setq sub-pts (append sub-pts (list p1)))
      (if (= i idx2)
        (setq continue nil)
        (progn
          (setq p2 (nth (rem (1+ i) n) base-pts-list))
          (setq sub-len (+ sub-len (distance p1 p2)))
          (setq i (rem (1+ i) n))
        )
      )
    )
    
    (setq offset-dist (/ (- target-area current-area) sub-len -1.0))
    (setq geom-offset (+ geom-offset offset-dist))
    (setq total-offset (- 0.0 geom-offset))
    (setq changed T)

    ;; 3. sub-pts를 기반으로 오프셋 선분들 생성
    (setq offset-lines nil j 0)
    (while (< j (1- (length sub-pts)))
      (setq p1 (nth j sub-pts) p2 (nth (1+ j) sub-pts) d-val (max 1e-9 (distance p1 p2)))
      (setq v-inward (list (/ (- (cadr p2) (cadr p1)) d-val) (/ (- (car p1) (car p2)) d-val)))
      (setq p1-off (list (+ (car p1) (* geom-offset (car v-inward))) (+ (cadr p1) (* geom-offset (cadr v-inward)))))
      (setq p2-off (list (+ (car p2) (* geom-offset (car v-inward))) (+ (cadr p2) (* geom-offset (cadr v-inward)))))
      (setq offset-lines (append offset-lines (list (list p1-off p2-off))))
      (setq j (1+ j))
    )

    ;; 4. 교차점을 통해 내부 정점들(final-seg) 계산 (양 끝점 P1, P2 제외)
    (setq final-seg nil j 0)
    (while (< j (1- (length offset-lines)))
      (setq L1 (nth j offset-lines) L2 (nth (1+ j) offset-lines))
      (setq p-new (util:intersect-lines (car L1) (cadr L1) (car L2) (cadr L2)))
      (if (not p-new) (setq p-new (cadr L1)))
      (setq final-seg (append final-seg (list p-new)))
      (setq j (1+ j))
    )

    ;; 5. 원본 리스트 업데이트: idx1 ~ idx2 사이를 final-seg로 교체
    (if (<= idx1 idx2)
      (progn
        (setq head nil i 0)
        (while (<= i idx1) (setq head (append head (list (nth i base-pts-list)))) (setq i (1+ i)))
        (setq tail nil i idx2)
        (while (< i n) (setq tail (append tail (list (nth i base-pts-list)))) (setq i (1+ i)))
        (setq new-pts-list (append head final-seg tail))
      )
      (progn
        (setq cnt-a (- n idx1 1)
              seg-a (vl-subseq-custom final-seg 0 cnt-a)
              seg-b (vl-subseq-custom final-seg cnt-a (length final-seg)))
        
        (setq tail nil i idx2)
        (while (<= i idx1) (setq tail (append tail (list (nth i base-pts-list)))) (setq i (1+ i)))
        (setq new-pts-list (append seg-b tail seg-a))
      )
    )

    (setq current-area (util:get-signed-area new-pts-list) iter (1+ iter))
  )

  (if changed 
    (list new-pts-list total-offset)
    nil
  )
)

(defun vl-subseq-custom (lst start end / res i)
  (setq i 0 res nil)
  (while (and lst (< i end)) (if (>= i start) (setq res (cons (car lst) res))) (setq lst (cdr lst) i (1+ i)))
  (reverse res)
)

;; 복제된 LWPOLYLINE의 정점을 강제 갱신 (2013 호환성 보강)
(defun util:rewrite-lwpoly-vertices (ent new-pts / ed out idx p)
  (setq ed (entget ent) out nil idx 0)
  (foreach d ed
    (if (= (car d) 10)
      (progn
        (setq p (nth idx new-pts))
        (setq out (cons (cons 10 (list (car p) (cadr p))) out))
        (setq idx (1+ idx))
      )
      (setq out (cons d out))
    )
  )
  (entmod (reverse out))
  (entupd ent)
)

;; 두 정점 목록이 사실상 동일한지 검사
(defun util:pts-same-p (pts1 pts2 tol / ok)
  (setq ok T)
  (if (/= (length pts1) (length pts2))
    (setq ok nil)
    (while (and ok pts1 pts2)
      (if (> (distance (car pts1) (car pts2)) tol)
        (setq ok nil)
      )
      (setq pts1 (cdr pts1) pts2 (cdr pts2))
    )
  )
  ok
)
;; ==========================================
;; [3] 메인 명령어 및 통합 UI
;; ==========================================

(defun fn:area-adjust-main (mode-name engine-fn /
                            *error* doc old-osmode ok
                            ent pts current-area
                            target-input target-area
                            p1 p2 idx1 idx2
                            new-pts flat-pts vla-orig vla-ref final-area
                            engine-res total-offset)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))
  (setq old-osmode (getvar "OSMODE"))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\n오류: " msg))
    )
    (setvar "OSMODE" old-osmode)
    (vla-EndUndoMark doc)
    (princ)
  )

  (vla-StartUndoMark doc)
  (setq ok T)

  (princ (strcat "\n[" mode-name " 모드 시작]"))
  (setq ent (car (entsel "\n조정할 폴리라인을 선택하세요: ")))
  (if (or (not ent) (/= (cdr (assoc 0 (entget ent))) "LWPOLYLINE"))
    (progn
      (princ "\n오류: LWPOLYLINE만 가능.")
      (setq ok nil)
    )
  )

  (if ok
    (progn
      (setq pts (util:ensure-clockwise (util:get-vertices ent))
            current-area (util:get-signed-area pts))
      (princ (strcat "\n현재 면적: " (rtos current-area 2 2)))

      (setq target-input (getstring "\n목표 면적 입력 (예: 123, @100, #200, d10, t150): ")
            target-area (util:parse-target current-area target-input))
      (if (not target-area)
        (progn
          (princ "\n오류: 잘못된 입력.")
          (setq ok nil)
        )
      )
    )
  )

  (if ok
    (progn
      (princ (strcat "\n설정된 목표 면적: " (rtos target-area 2 2)))
      (setvar "OSMODE" 1)
      (setq p1 (getpoint "\n구간 시작점 P1 클릭: ")
            idx1 (util:get-nearest-vertex-idx p1 pts 0.001)
            p2 (getpoint "\n구간 끝점 P2 클릭: ")
            idx2 (util:get-nearest-vertex-idx p2 pts 0.001))
      (setvar "OSMODE" old-osmode)

      (if (or (< idx1 0) (< idx2 0) (= idx1 idx2))
        (progn
          (princ "\n오류: P1/P2가 동일 정점으로 인식되었습니다. 서로 다른 꼭지점을 선택하세요.")
          (setq ok nil)
        )
      )

      (if ok
        (progn
          (princ "\n연산 중...")
          (setq engine-res (apply engine-fn (list pts idx1 idx2 target-area)))
          (if engine-res
            (setq new-pts (car engine-res) total-offset (cadr engine-res))
            (setq new-pts nil)
          )

          (if (not new-pts)
            (princ "\n오류: 선택 구간의 교차점 계산에 실패했습니다. P1/P2를 인접 모서리가 분명한 꼭지점으로 다시 선택하세요.")
            (if (util:pts-same-p pts new-pts 0.0001)
              (princ "\n오류: 계산 결과가 원본과 동일합니다. P1/P2 선택 또는 목표 면적을 다시 확인하세요.")
              (progn
                (setq vla-orig (vlax-ename->vla-object ent)
                      vla-ref (vla-copy vla-orig)
                      flat-pts nil)
                (foreach v new-pts
                  (setq flat-pts (append flat-pts (list (car v) (cadr v))))
                )
                (vlax-put-property vla-ref 'Coordinates
                  (vlax-make-variant
                    (vlax-safearray-fill
                      (vlax-make-safearray vlax-vbDouble (cons 0 (1- (length flat-pts))))
                      flat-pts
                    )
                  )
                )
                (vla-put-color vla-ref 3)
                (util:rewrite-lwpoly-vertices (vlax-vla-object->ename vla-ref) new-pts)

                (setq final-area (util:get-signed-area new-pts))
                (if (<= (abs (- target-area final-area)) 0.01)
                  (princ (strcat "\n[완료] 수정면적: " (rtos final-area 2 2) ", 이동량: " (rtos total-offset 2 4)))
                  (princ (strcat "\n[중단] 30회 초과. 수정면적: "
                                 (rtos final-area 2 2)
                                 ", 이동량: " (rtos total-offset 2 4)
                                 " (오차: "
                                 (rtos (abs (- target-area final-area)) 2 4)
                                 ")"))
                )
              )
            )
          )
        )
      )
    )
  )

  (setvar "OSMODE" old-osmode)
  (vla-EndUndoMark doc)
  (princ)
)
;; 명령어 1: 구간 이동 방식
(defun C:AREA_ADJUST () (fn:area-adjust-main "구간 이동" 'fn:adjust-area-move))

;; 명령어 2: 고정점 방식
(defun C:AREA_ADJUST_FIXED () (fn:area-adjust-main "고정점 유지" 'fn:adjust-area-fixed))

(princ "\n면적 정밀 조정 도구(통합) 로드 완료.")
(princ)























