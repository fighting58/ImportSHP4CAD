;;; CoordRound.lsp
;;; 좌표 오사오입 재결정 (Round half to even)
;;; AutoCAD 2013 & Windows 11 환경 최적화

(vl-load-com)

;; ==========================================
;; [1] 공통 유틸리티 함수 (util:)
;; ==========================================

;; Banker's Rounding(오사오입): .5 값은 가장 가까운 짝수로 반올림
(defun util:round-half-to-even (num prec / factor shifted integral fractional)
  (setq factor (expt 10.0 prec))
  (setq shifted (* num factor))
  (setq integral (fix (if (>= shifted 0) (+ shifted 1e-9) (- shifted 1e-9))))
  (setq fractional (abs (- shifted integral)))

  (if (equal fractional 0.5 1e-7)
    (if (= (rem (abs integral) 2) 0)
      (/ (float integral) factor)
      (/ (float (+ integral (if (>= shifted 0) 1 -1))) factor)
    )
    (/ (atof (rtos shifted 2 0)) factor)
  )
)

;; 좌표 리스트 전체에 오사오입 규칙 적용
(defun util:round-list-half-to-even (lst prec)
  (mapcar '(lambda (x) (util:round-half-to-even x prec)) lst)
)

;; ==========================================
;; [2] 객체 처리 엔진 (fn:)
;; ==========================================

(defun fn:coord-round-engine (prec / ss i ent vla-obj obj-name old-coords new-coords p1 p2 count ac-doc vla-ref)
  (setq ac-doc (vla-get-activedocument (vlax-get-acad-object)))
  (vla-startundomark ac-doc)

  (princ (strcat "\n[좌표 오사오입 변환 - 소수점 " (itoa prec) "자리]"))
  (setq ss (ssget '((0 . "POINT,LINE,LWPOLYLINE,CIRCLE,ARC,TEXT,MTEXT"))))

  (if (not ss)
    (progn
      (princ "\n선택된 객체가 없습니다.")
      (vla-endundomark ac-doc)
      (exit)
    )
  )

  (setq count 0 i 0)
  (repeat (sslength ss)
    (setq ent (ssname ss i))
    (setq vla-obj (vlax-ename->vla-object ent))
    (setq obj-name (vla-get-objectname vla-obj))

    ;; 원본은 보존하고, 변환 결과는 녹색 ref_object로 생성
    (setq vla-ref (vla-copy vla-obj))
    (vla-put-color vla-ref 3)

    (cond
      ((= obj-name "AcDbPolyline")
       (setq old-coords (vlax-get vla-ref 'Coordinates))
       (setq new-coords (util:round-list-half-to-even old-coords prec))
       (vlax-put vla-ref 'Coordinates new-coords)
      )

      ((= obj-name "AcDbLine")
       (setq p1 (vlax-get vla-ref 'StartPoint))
       (setq p2 (vlax-get vla-ref 'EndPoint))
       (vla-put-startpoint vla-ref (vlax-3d-point (util:round-list-half-to-even p1 prec)))
       (vla-put-endpoint vla-ref (vlax-3d-point (util:round-list-half-to-even p2 prec)))
      )

      ((member obj-name '("AcDbPoint" "AcDbText" "AcDbMText" "AcDbCircle" "AcDbArc"))
       (if (member obj-name '("AcDbCircle" "AcDbArc"))
         (vla-put-center vla-ref
           (vlax-3d-point
             (util:round-list-half-to-even (vlax-get vla-ref 'Center) prec)
           )
         )
         (vla-put-insertionpoint vla-ref
           (vlax-3d-point
             (util:round-list-half-to-even (vlax-get vla-ref 'InsertionPoint) prec)
           )
         )
       )
      )
    )

    (setq count (1+ count))
    (setq i (1+ i))
  )

  (vla-endundomark ac-doc)
  (princ (strcat "\n완료: 총 " (itoa count) "개의 객체 생성."))
  (princ)
)

;; ==========================================
;; [3] 실행 명령어
;; ==========================================

(defun C:COORDROUND_OSA2 () (fn:coord-round-engine 2))
(defun C:COORDROUND_OSA3 () (fn:coord-round-engine 3))
(defun C:OSA2 () (C:COORDROUND_OSA2))
(defun C:OSA3 () (C:COORDROUND_OSA3))

(princ "\n좌표 오사오입 재결정 로드 완료.")
(princ)


