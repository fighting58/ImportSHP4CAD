;;; ArcToPolyline.lsp
;;; 호(Arc)를 선분 폴리라인으로 변환 (근사화)
;;; 1. 등분할(N) 변환
;;; 2. 화살고(M, 최대 5cm) 기준 변환
;;; 3. 현 길이(D) 기준 변환
;;;
;;; 결과: 현재 레이어에 노란색(Color 2)으로 새로운 객체 생성.
;;; [Rule 3 적용]: 원본 보존을 위해 결과 객체는 노란색(Color 2)으로 구분.
;;; (참고: 녹색 Color 3은 면적 조정 등에서 사용)

(vl-load-com)

;; ==========================================
;; [1] 호 분할 계산 유틸리티 (util:)
;; ==========================================

;; 호 세그먼트의 분할 정점 리스트 생성
(defun util:get-arc-segmented-points (obj start-param end-param mode val / 
                      dist-start dist-end total-dist n seg-dist pts p i radius ang bulge p1 p2 dist-p1p2 x seg-ang)
  (setq dist-start (vlax-curve-getDistAtParam obj start-param)
        dist-end   (vlax-curve-getDistAtParam obj end-param)
        total-dist (abs (- dist-end dist-start))
  )

  ;; Mode M(화살고) 및 D(현 길이) 대응을 위한 반지름 계산
  (if (or (= mode "M") (= mode "D"))
    (if (= (vla-get-objectname obj) "AcDbArc")
      (setq radius (vla-get-radius obj)
            ang    (vla-get-totalangle obj))
      (progn
        ;; 폴리라인 세그먼트의 경우
        (setq bulge (vla-getbulge obj (fix start-param)))
        (setq p1 (vlax-curve-getPointAtParam obj start-param))
        (setq p2 (vlax-curve-getPointAtParam obj end-param))
        (setq dist-p1p2 (distance (list (car p1) (cadr p1)) (list (car p2) (cadr p2))))
        (setq ang (* 4.0 (atan (abs bulge))))
        (setq radius (/ (/ dist-p1p2 2.0) (sin (/ ang 2.0))))
      )
    )
  )

  (cond
    ;; Mode N: 단순 등분할
    ((= mode "N") (setq n val))
    
    ;; Mode M: 화살고 기준 (h <= val)
    ((= mode "M")
     (if (<= radius val)
       (setq n 1)
       (progn
         (setq x (- 1.0 (/ val radius)))
         (setq seg-ang (* 2.0 (atan (sqrt (- 1.0 (* x x))) x)))
         (setq n (fix (max 1 (abs (/ ang seg-ang)))))
         (if (> (rem (abs ang) seg-ang) 0.0001) (setq n (1+ n)))
       )
     )
    )

    ;; Mode D: 현 길이 기준 (d)
    ((= mode "D")
     (if (<= val (* 2.0 radius))
       (progn
         (setq x (/ val (* 2.0 radius)))
         (setq seg-ang (* 2.0 (atan x (sqrt (- 1.0 (* x x))))))
         (setq n (fix (/ ang seg-ang)))
       )
       (setq n 1)
     )
    )
  )

  ;; 정점 추출
  (if (= mode "D")
    (progn
      (setq pts (list (vlax-curve-getPointAtParam obj start-param)))
      (setq i 1 seg-dist (+ dist-start (* i val)))
      (while (< (+ seg-dist 1e-6) dist-end)
        (setq p (vlax-curve-getPointAtDist obj seg-dist))
        (setq pts (cons p pts))
        (setq i (1+ i) seg-dist (+ dist-start (* i val)))
      )
      (setq pts (cons (vlax-curve-getPointAtParam obj end-param) pts))
      (setq pts (reverse pts))
    )
    (progn
      (setq pts nil i 0)
      (while (<= i n)
        (setq seg-dist (+ dist-start (* (/ i (float n)) total-dist)))
        (if (> seg-dist dist-end) (setq seg-dist dist-end))
        (setq p (vlax-curve-getPointAtDist obj seg-dist))
        (setq pts (cons p pts))
        (setq i (1+ i))
      )
      (setq pts (reverse pts))
    )
  )
  pts
)

;; 정점 리스트로 노란색 LWPolyline 생성
(defun util:create-ref-polyline (pts / ac-doc space obj coords i)
  (setq ac-doc (vla-get-activedocument (vlax-get-acad-object)))
  (setq space (vla-get-modelspace ac-doc))
  (setq coords (vlax-make-safearray vlax-vbDouble (cons 0 (1- (* (length pts) 2)))))
  (setq i 0)
  (foreach p pts
    (vlax-safearray-put-element coords i (car p))
    (vlax-safearray-put-element coords (1+ i) (cadr p))
    (setq i (+ i 2))
  )
  (setq obj (vla-addlightweightpolyline space coords))
  (vla-put-color obj 3)
  obj
)

;; ==========================================
;; [2] 메인 엔진 (C:A2P)
;; ==========================================
(defun C:ARCTOPOLYLINE (/ *error* ss mode val i ent data type old-cmdecho old-osmode count ac-doc vla-obj j num-segs bulge p1 seg-pts pts new-poly)
  (setq ac-doc (vla-get-activedocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort"))) (princ (strcat "\n오류: " msg)))
    (if old-cmdecho (setvar "CMDECHO" old-cmdecho))
    (if old-osmode (setvar "OSMODE" old-osmode))
    (vla-endundomark ac-doc)
    (princ)
  )

  (princ "\n[호 변환 도구: Arc to Polyline]")
  (setq ss (ssget '((0 . "ARC,LWPOLYLINE"))))
  (if (not ss) (progn (princ "\n선택된 객체가 없습니다.") (exit)))

  (initget "Number Midordinate Distance")
  (setq mode (getkword "\n변환 옵션 선택 [등분할(N)/중앙종거(M)/현길이(D)]: "))
  (if (not mode) (setq mode "Number"))

  (cond
    ((= mode "Number") 
     (setq mode "N" val (getint "\n분할 수 입력 <10>: "))
     (if (not val) (setq val 10)))
    ((= mode "Midordinate")
     (setq mode "M" val (getdist "\n최대 중앙종거(m) 입력 <0.05>: "))
     (if (not val) (setq val 0.05)))
    ((= mode "Distance")
     (setq mode "D" val (getdist "\n현 길이(m) 입력 <1.0>: "))
     (if (not val) (setq val 1.0)))
  )

  (setq old-cmdecho (getvar "CMDECHO") old-osmode (getvar "OSMODE"))
  (setvar "CMDECHO" 0)
  (setvar "OSMODE" 0)
  (vla-startundomark ac-doc)

  (setq count 0 i 0)
  (repeat (sslength ss)
    (setq ent (ssname ss i) data (entget ent) type (cdr (assoc 0 data)))
    (setq vla-obj (vlax-ename->vla-object ent))
    (cond
      ((= type "ARC")
       (setq pts (util:get-arc-segmented-points vla-obj (vlax-curve-getStartParam vla-obj) (vlax-curve-getEndParam vla-obj) mode val))
       (util:create-ref-polyline pts)
       (setq count (1+ count)))
      ((= type "LWPOLYLINE")
       (setq pts nil j 0 num-segs (fix (vlax-curve-getEndParam vla-obj)))
       (while (< j num-segs)
         (setq bulge (vla-getbulge vla-obj j) p1 (vlax-curve-getPointAtParam vla-obj j))
         (if (= bulge 0.0)
           (setq pts (cons p1 pts))
           (progn
             (setq seg-pts (util:get-arc-segmented-points vla-obj j (1+ j) mode val))
             (setq seg-pts (reverse (cdr (reverse seg-pts))))
             (setq pts (append (reverse seg-pts) pts))
           )
         )
         (setq j (1+ j))
       )
       (if (= (vla-get-closed vla-obj) :vlax-true)
         (setq new-poly (util:create-ref-polyline (reverse pts)))
         (progn
           (setq pts (cons (vlax-curve-getPointAtParam vla-obj num-segs) pts))
           (setq new-poly (util:create-ref-polyline (reverse pts)))
         )
       )
       (if (= (vla-get-closed vla-obj) :vlax-true) (vla-put-closed new-poly :vlax-true))
       (setq count (1+ count)))
    )
    (setq i (1+ i))
  )

  (vla-endundomark ac-doc)
  (setvar "CMDECHO" old-cmdecho) (setvar "OSMODE" old-osmode)
  (princ (strcat "\n완료: 총 " (itoa count) "개의 객체가 변환되었습니다."))
  (princ)
)

(princ "\n호 변환 도구 로드 완료.")
(princ)


