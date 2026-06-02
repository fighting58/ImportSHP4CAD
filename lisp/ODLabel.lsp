;;; ODLabel.lsp
;;; LWPOLYLINE의 OD값을 읽어 시각적 중심(Polylabel)에 최적의 각도로 레이블 생성
;;; AutoCAD Map 3D 2013 전용

(vl-load-com)

;; ==========================================
;; [1] 유틸리티 함수
;; ==========================================

;; 정점 리스트 추출
(defun util:get-poly-points (ent / pts)
  (foreach x (entget ent)
    (if (= (car x) 10) (setq pts (cons (cdr x) pts)))
  )
  (reverse pts)
)

;; 점에서 선분까지의 최단 거리
(defun util:dist-to-segment (p p1 p2 / dx dy t-param)
  (setq dx (- (car p2) (car p1))
        dy (- (cadr p2) (cadr p1)))
  (if (and (= dx 0) (= dy 0))
    (distance p p1)
    (progn
      (setq t-param (/ (+ (* (- (car p) (car p1)) dx) (* (- (cadr p) (cadr p1)) dy)) (+ (* dx dx) (* dy dy))))
      (cond
        ((< t-param 0) (distance p p1))
        ((> t-param 1) (distance p p2))
        (t (distance p (list (+ (car p1) (* t-param dx)) (+ (cadr p1) (* t-param dy)))))
      )
    )
  )
)

;; 점이 폴리라인 내부에 있는지 확인
(defun util:is-inside (pt pts / i j inside p1 p2)
  (setq i 0 j (1- (length pts)) inside nil)
  (while (< i (length pts))
    (setq p1 (nth i pts) p2 (nth j pts))
    (if (and (or (and (<= (cadr p1) (cadr pt)) (< (cadr pt) (cadr p2)))
                 (and (<= (cadr p2) (cadr pt)) (< (cadr pt) (cadr p1))))
             (< (car pt) (+ (car p1) (/ (* (- (car p2) (car p1)) (- (cadr pt) (cadr p1))) (- (cadr p2) (cadr p1))))))
      (setq inside (not inside))
    )
    (setq j i i (1+ i))
  )
  inside
)

;; 점에서 폴리라인 경계까지의 거리
(defun util:dist-to-poly (pt pts / min-dist i p1 p2 d)
  (setq min-dist 1e12 i 0)
  (while (< i (length pts))
    (setq p1 (nth i pts) p2 (nth (rem (1+ i) (length pts)) pts))
    (setq d (util:dist-to-segment pt p1 p2))
    (if (< d min-dist) (setq min-dist d))
    (setq i (1+ i))
  )
  (if (util:is-inside pt pts) min-dist (* -1.0 min-dist))
)

;; 직선(점+각도)과 선분(p1, p2)의 교차 거리 계산
(defun util:intersect-ray-dist (pc ang p1 p2 / dx dy v1x v1y v2x v2y det t-param u-param)
  (setq v1x (cos ang) v1y (sin ang)
        v2x (- (car p2) (car p1)) v2y (- (cadr p2) (cadr p1))
        det (- (* v1x v2y) (* v1y v2x)))
  (if (not (equal det 0.0 1e-9))
    (progn
      (setq t-param (/ (- (* (- (car p1) (car pc)) v2y) (* (- (cadr p1) (cadr pc)) v2x)) det)
            u-param (/ (- (* (- (car p1) (car pc)) v1y) (* (- (cadr p1) (cadr pc)) v1x)) det))
      (if (and (> t-param 0) (>= u-param 0) (<= u-param 1)) t-param nil)
    )
    nil
  )
)

;; 특정 지점에서 가장 긴 방향(각도) 찾기
(defun util:get-best-angle (pc pts / best-ang max-len ang i p1 p2 d len t-val min-t found)
  (setq best-ang 0.0 max-len 0.0 ang 0.0)
  (repeat 18 ; 0~170도 탐색 (10도 간격)
    (setq len 0.0)
    ;; 두 방향(ang, ang+PI)으로의 최단 교차 거리 합산
    (foreach a (list ang (+ ang pi))
      (setq min-t 1e12 found nil i 0)
      (while (< i (length pts))
        (setq p1 (nth i pts) p2 (nth (rem (1+ i) (length pts)) pts))
        (setq t-val (util:intersect-ray-dist pc a p1 p2))
        (if (and t-val (< t-val min-t)) (setq min-t t-val found T))
        (setq i (1+ i))
      )
      (if found (setq len (+ len min-t)))
    )
    (if (> len max-len) (setq max-len len best-ang ang))
    (setq ang (+ ang (/ pi 18.0)))
  )
  ;; 가독성을 위해 90~270도 사이면 180도 회전
  (if (and (> best-ang (/ pi 2.0)) (<= best-ang (/ (* 3.0 pi) 2.0)))
    (setq best-ang (- best-ang pi))
  )
  best-ang
)

;; ==========================================
;; [2] Polylabel 엔진 (Visual Center 찾기)
;; ==========================================

(defun make-cell (x y size pts / d)
  (setq d (util:dist-to-poly (list x y) pts))
  (list (+ d (* size 0.7071)) d x y size)
)

(defun fn:polylabel (ent precision / pts bbox p1 p2 minpt maxpt min-x min-y max-x max-y width height size cell-queue best-cell cell x y)
  (setq pts (util:get-poly-points ent))
  (vla-getboundingbox (vlax-ename->vla-object ent) 'minpt 'maxpt)
  (setq p1 (vlax-safearray->list minpt) p2 (vlax-safearray->list maxpt))
  (setq min-x (car p1) min-y (cadr p1) max-x (car p2) max-y (cadr p2))
  (setq width (- max-x min-x) height (- max-y min-y))
  (setq size (max width height))
  (setq best-cell (make-cell (+ min-x (* width 0.5)) (+ min-y (* height 0.5)) 0 pts))
  (setq cell-queue nil x min-x)
  (while (< x max-x)
    (setq y min-y)
    (while (< y max-y)
      (setq cell-queue (cons (make-cell (+ x (* size 0.5)) (+ y (* size 0.5)) size pts) cell-queue))
      (setq y (+ y size))
    )
    (setq x (+ x size))
  )
  (while (and cell-queue (> (nth 4 (car cell-queue)) precision))
    (setq cell-queue (vl-sort cell-queue '(lambda (a b) (> (car a) (car b)))))
    (setq cell (car cell-queue) cell-queue (cdr cell-queue))
    (if (> (nth 1 cell) (nth 1 best-cell)) (setq best-cell cell))
    (setq size (* (nth 4 cell) 0.5) x (nth 2 cell) y (nth 3 cell))
    (setq cell-queue (cons (make-cell (- x (* size 0.5)) (- y (* size 0.5)) size pts) cell-queue))
    (setq cell-queue (cons (make-cell (+ x (* size 0.5)) (- y (* size 0.5)) size pts) cell-queue))
    (setq cell-queue (cons (make-cell (- x (* size 0.5)) (+ y (* size 0.5)) size pts) cell-queue))
    (setq cell-queue (cons (make-cell (+ x (* size 0.5)) (+ y (* size 0.5)) size pts) cell-queue))
  )
  (list (nth 2 best-cell) (nth 3 best-cell))
)

;; ==========================================
;; [3] 메인 명령어
;; ==========================================

(defun C:ODLABEL (/ *error* ss i ent tables field val center ang pts old-cmdecho doc count found-table temp-val tbl ok)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\n오류: " msg))
    )
    (setvar "CMDECHO" old-cmdecho)
    (vla-EndUndoMark doc)
    (princ "\n작업이 중단되었습니다.")
    (princ)
  )

  (vla-StartUndoMark doc)
  (setq old-cmdecho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  (setq ok T)

  ;; 1. 객체 선택
  (princ "\n레이블을 생성할 폴리라인들을 선택하세요.")
  (setq ss (ssget '((0 . "LWPOLYLINE"))))
  (if (not ss)
    (progn
      (princ "\n선택된 객체가 없습니다.")
      (setq ok nil)
    )
  )

  ;; 2. 필드 이름 입력
  (if ok
    (progn
      (setq field (getstring T "\n출력할 필드 이름을 입력하세요 (모든 테이블 검색): "))
      (if (= field "")
        (progn
          (princ "\n필드 이름이 입력되지 않았습니다.")
          (setq ok nil)
        )
      )
    )
  )

  ;; 3. 루프 처리
  (if ok
    (progn
      (princ (strcat "\n'" field "' 필드 검색 및 레이블 생성 중..."))
      (setq i 0 count 0)
      (repeat (sslength ss)
        (setq ent (ssname ss i))
        (setq val nil found-table nil)

        ;; 현재 객체에 결합된 모든 테이블 목록 가져오기
        (setq tables (ade_odgettables ent))

        ;; 테이블 순회하며 필드 존재 여부 확인
        (if tables
          (foreach tbl tables
            (if (and (not found-table) (ade_odfielddefn tbl field))
              (progn
                (setq temp-val (ade_odgetfield ent tbl field 0))
                (if (and temp-val (/= (vl-princ-to-string temp-val) ""))
                  (progn
                    (setq val (vl-princ-to-string temp-val))
                    (setq found-table tbl)
                  )
                )
              )
            )
          )
        )

        ;; 값을 찾은 경우에만 레이블 생성
        (if (and found-table val)
          (progn
            (setq center (fn:polylabel ent 0.1))
            (setq pts (util:get-poly-points ent))
            (setq ang (util:get-best-angle center pts))

            (entmake (list
              '(0 . "TEXT")
              '(100 . "AcDbEntity")
              '(100 . "AcDbText")
              (cons 1 val)
              (cons 10 center)
              (cons 11 center)
              '(40 . 1.0)
              (cons 50 ang)
              '(72 . 1)
              '(73 . 2)
            ))
            (setq count (1+ count))
          )
        )
        (setq i (1+ i))
      )
    )
  )

  (setvar "CMDECHO" old-cmdecho)
  (vla-EndUndoMark doc)
  (if ok
    (princ (strcat "\n완료: 총 " (itoa (sslength ss)) "개 중 " (itoa count) "개의 레이블이 생성되었습니다."))
  )
  (princ)
)
(princ "\nOD 레이블 생성 도구(필드 통합 검색) 로드 완료.")
(princ)


