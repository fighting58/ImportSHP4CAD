;;; OuterBoundary.lsp
;;; 선택된 객체(라인, 폴리라인)로 둘러싸인 영역의 최외곽선을 폴리라인으로 생성
;;; AutoCAD 2013 & Windows 11 환경 최적화 (프리징 방지)

(vl-load-com)

;; [Command Wrapper]
(defun C:OUTERBOUND (/ ss result)
  (princ "\n외곽선을 추출할 라인 또는 폴리라인을 선택하세요.")
  (setq ss (ssget '((0 . "LINE,LWPOLYLINE,POLYLINE"))))
  (if ss
    (progn
      (setq result (fn:outerbound ss))
      (cond
        ((eq result 'stopped) (princ))
        (result (princ "\n성공: 최외곽 폴리라인이 생성되었습니다."))
        (T (princ "\n오류: 외곽선 생성에 실패했습니다."))
      )
    )
    (princ "\n선택된 객체가 없습니다.")
  )
  (princ)
)

;; [Core Function]
;; ss: selection set of lines/polylines
;; returns: ename of the final polyline or nil
(defun util:collect-new-entities-by-type (start-ent type-name / s e)
  (setq s (ssadd))
  (setq e (if start-ent (entnext start-ent) (entnext)))
  (while e
    (if (= (cdr (assoc 0 (entget e))) type-name)
      (setq s (ssadd e s))
    )
    (setq e (entnext e))
  )
  s
)


(defun util:collect-new-polylines (start-ent / s e tname)
  (setq s (ssadd))
  (setq e (if start-ent (entnext start-ent) (entnext)))
  (while e
    (setq tname (cdr (assoc 0 (entget e))))
    (if (or (= tname "LWPOLYLINE") (= tname "POLYLINE"))
      (setq s (ssadd e s))
    )
    (setq e (entnext e))
  )
  s
)
(defun fn:outerbound (ss / *error* acadObj doc fuzz old-cmdecho old-peditaccept regionSS finalRegion explodedSS resultSS finalPoly ok stop-flag i e obj area max-area mark workSS srcObj copyObj)
  (setq fuzz 0.0005)
  (setq acadObj (vlax-get-acad-object))
  (setq doc (vla-get-ActiveDocument acadObj))
  (setq old-cmdecho (getvar "CMDECHO"))
  (setq old-peditaccept (getvar "PEDITACCEPT"))

  (defun *error* (msg)
    (setvar "CMDECHO" old-cmdecho)
    (setvar "PEDITACCEPT" old-peditaccept)
    (vla-EndUndoMark doc)
    (command "_.undo" "1")
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\n오류 발생: " msg))
    )
    nil
  )

  (vla-StartUndoMark doc)
  (setvar "CMDECHO" 0)
  (setq ok T finalPoly nil stop-flag nil)
  (setq workSS (ssadd))
  (setq i 0)
  (repeat (sslength ss)
    (setq e (ssname ss i)
          srcObj (vlax-ename->vla-object e)
          copyObj (vla-copy srcObj))
    (ssadd (vlax-vla-object->ename copyObj) workSS)
    (setq i (1+ i))
  )
  (setq mark (entlast))
  (command "_.region" workSS "")
  (setq regionSS (util:collect-new-entities-by-type mark "REGION"))
  (if (> (sslength regionSS) 0)
    (progn
    )
    (progn
      (setq ok nil)
    )
  )
  (if ok
    (progn
      (if (> (sslength regionSS) 1)
        (progn
          (setq mark (entlast))
          (command "_.union" regionSS "")

          ;; UNION 결과 수집: 신규 생성, PickPrevious, entlast 순으로 탐색
          (setq regionSS (util:collect-new-entities-by-type mark "REGION"))
          (if (= (sslength regionSS) 0)
            (setq regionSS (ssget "_P" '((0 . "REGION"))))
          )
          (if (or (not regionSS) (= (sslength regionSS) 0))
            (progn
              (setq regionSS (ssadd))
              (if (and (entlast) (= (cdr (assoc 0 (entget (entlast)))) "REGION"))
                (ssadd (entlast) regionSS)
              )
            )
          )
        )
      )

      (if (and regionSS (> (sslength regionSS) 0))
        (progn
          (if (> (sslength regionSS) 1)
            (progn
              (princ "\n두 개 이상의 분리된 영역이 생성되어 처리를 종료합니다")
              (setq ok nil stop-flag T)
            )
          )
        )
        (progn
          (setq ok nil)
        )
      )
    )
  )
  (if ok
    (progn
      (setq i 0 max-area -1.0 finalRegion nil)
      (repeat (sslength regionSS)
        (setq e (ssname regionSS i)
              obj (vlax-ename->vla-object e)
              area (vla-get-Area obj))
        (if (> area max-area)
          (setq max-area area finalRegion e)
        )
        (setq i (1+ i))
      )
      (if finalRegion
        (progn
          (setq mark (entlast))
          (command "_.explode" finalRegion)
          (setq explodedSS (ssget "_P" '((0 . "LINE,ARC,LWPOLYLINE,POLYLINE"))))
          (if (not explodedSS)
            (setq explodedSS (util:collect-new-entities-by-type mark "LINE"))
          )
          (if (or (not explodedSS) (= (sslength explodedSS) 0))
            (progn
              (princ "\n두 개 이상의 분리된 영역이 생성되어 처리를 종료합니다")
              (setq ok nil stop-flag T)
            )
          )
        )
        (progn
          (setq ok nil)
        )
      )
    )
  )
  (if ok
    (progn
      (if (not explodedSS)
        (setq explodedSS (ssget "_P" '((0 . "LINE,ARC,LWPOLYLINE,POLYLINE"))))
      )
      (if (and explodedSS (> (sslength explodedSS) 0))
        (progn
          (setvar "PEDITACCEPT" 1)
          (setq mark (entlast))
          (command "_.pedit" "_m" explodedSS "" "_j" fuzz "")

          ;; 조인 결과 수집: Previous -> 신규 엔티티 추적 -> JOIN fallback
          (setq resultSS (ssget "_P" '((0 . "*POLYLINE"))))
          (if (or (not resultSS) (= (sslength resultSS) 0))
            (setq resultSS (util:collect-new-polylines mark))
          )
          (if (or (not resultSS) (= (sslength resultSS) 0))
            (progn
              (setq mark (entlast))
              (command "_.join" explodedSS "")
              (setq resultSS (util:collect-new-polylines mark))
            )
          )

          (if (or (not resultSS) (= (sslength resultSS) 0))
            (setq ok nil)
          )
          (if (and resultSS (> (sslength resultSS) 1))
            (progn
              (princ "\n두 개 이상의 분리된 영역이 생성되어 처리를 종료합니다")
              (setq ok nil stop-flag T)
            )
          )
        )
        (progn
          (setq ok nil)
        )
      )
    )
  )
  (if (and ok resultSS)
    (progn
      (setq i 0 max-area -1.0 finalPoly nil)
      (repeat (sslength resultSS)
        (setq e (ssname resultSS i)
              obj (vlax-ename->vla-object e)
              area 0.0)
        (if (= (vla-get-Closed obj) :vlax-true)
          (setq area (vla-get-Area obj))
        )
        (if (> area max-area)
          (setq max-area area finalPoly e)
        )
        (setq i (1+ i))
      )
      (if finalPoly
        (progn
          (vla-put-Color (vlax-ename->vla-object finalPoly) 3)
        )
        (progn
          (setq ok nil)
        )
      )
    )
  )

  (if (not finalPoly)
    (progn
      (vla-EndUndoMark doc)
      (command "_.undo" "1")
    )
    (vla-EndUndoMark doc)
  )

  (setvar "CMDECHO" old-cmdecho)
  (setvar "PEDITACCEPT" old-peditaccept)
  (if stop-flag 'stopped finalPoly)
)
(princ "\n외곽선 추출 도구 로드 완료.")
(princ)

















