;;; REFPOLY.lsp
;;; 폴리라인 방향 정렬 및 구간 교체

(vl-load-com)
(if (not fn:outerbound) (load "OuterBoundary.lsp"))

(defun util:get-vertices (ent / pts)
  (foreach x (entget ent)
    (if (= (car x) 10) (setq pts (cons (cdr x) pts)))
  )
  (reverse pts)
)

(defun util:get-signed-area (pts / area p1 p2)
  (setq area 0.0 pts (append pts (list (car pts))))
  (while (and (setq p1 (car pts)) (setq p2 (cadr pts)))
    (setq area (+ area (* (- (car p2) (car p1)) (+ (cadr p2) (cadr p1)))))
    (setq pts (cdr pts))
  )
  (* 0.5 area)
)

(defun util:ensure-clockwise (pts)
  (if (< (util:get-signed-area pts) 0) (reverse pts) pts)
)

(defun util:ensure-counter-clockwise (pts)
  (if (> (util:get-signed-area pts) 0) (reverse pts) pts)
)

(defun util:get-segment-by-list-order (pts idx1 idx2 / n res i)
  (setq n (length pts) res nil i idx1)
  (while (/= i idx2)
    (setq res (cons (nth i pts) res) i (rem (1+ i) n))
  )
  (setq res (cons (nth idx2 pts) res))
  (reverse res)
)

(defun util:refpoly-get-nearest-vertex-idx (pt pts / min-dist res-idx i d)
  (setq i 0 min-dist 1e9 res-idx -1)
  (foreach v pts
    (setq d (distance pt v))
    (if (< d min-dist) (setq min-dist d res-idx i))
    (setq i (1+ i))
  )
  res-idx
)

(defun util:pts-same-p (pts1 pts2 tol / ok)
  (setq ok T)
  (if (/= (length pts1) (length pts2))
    (setq ok nil)
    (while (and ok pts1 pts2)
      (if (> (distance (car pts1) (car pts2)) tol) (setq ok nil))
      (setq pts1 (cdr pts1) pts2 (cdr pts2))
    )
  )
  ok
)

(defun util:subseq (lst start end / res i)
  (setq res nil i 0)
  (while lst
    (if (and (>= i start) (< i end)) (setq res (cons (car lst) res)))
    (setq lst (cdr lst) i (1+ i))
  )
  (reverse res)
)

(defun util:replace-lwpoly-vertices (ent pts / ed newed)
  (setq ed (entget ent) newed nil)
  (while ed
    (if (/= (caar ed) 10) (setq newed (cons (car ed) newed)))
    (setq ed (cdr ed))
  )
  (setq newed (reverse newed))
  (if (assoc 90 newed)
    (setq newed (subst (cons 90 (length pts)) (assoc 90 newed) newed))
    (setq newed (append newed (list (cons 90 (length pts)))))
  )
  (foreach p pts
    (setq newed (append newed (list (cons 10 (list (car p) (cadr p))))))
  )
  (entmod newed)
  (entupd ent)
)

(defun fn:refpoly-engine (mode-name target-norm-fn ref-norm-fn /
                          *error* doc old-osmode ok changed
                          target-ent ref-ent target-pts ref-pts ss-ref
                          p1 p2 q1 q2 idxP1 idxP2 idxQ1 idxQ2
                          idxP1-prev idxP2-next cntA segA segB seg-q rem-p new-pts)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object))
        old-osmode (getvar "OSMODE"))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\n오류: " msg))
    )
    (setvar "OSMODE" old-osmode)
    (vla-EndUndoMark doc)
    (princ)
  )

  (vla-StartUndoMark doc)
  (setq ok T changed nil)
  (princ (strcat "\n[" mode-name " 시작]"))

  (setq target-ent (car (entsel "\n기준 폴리라인 선택: ")))
  (if (or (not target-ent) (/= (cdr (assoc 0 (entget target-ent))) "LWPOLYLINE"))
    (progn (princ "\n오류: LWPOLYLINE을 선택하세요.") (setq ok nil))
  )

  (if ok
    (progn
      (princ "\n참조 영역 선택: ")
      (setq ss-ref (ssget))
      (if (not ss-ref) (progn (princ "\n오류: 참조할 객체가 없습니다.") (setq ok nil)))
    )
  )

  (if ok
    (progn
      (setq ref-ent (fn:outerbound ss-ref))
      (if (or (not ref-ent) (eq ref-ent 'stopped))
        (progn (princ "\n오류: 외곽선을 생성할 수 없습니다.") (setq ok nil))
      )
    )
  )

  (if ok
    (progn
      (setq ref-pts (apply ref-norm-fn (list (util:get-vertices ref-ent)))
            target-pts (apply target-norm-fn (list (util:get-vertices target-ent))))
      (setvar "OSMODE" 1)
      (setq p1 (getpoint "\nP1 클릭: ") q1 (getpoint "\nQ1 클릭: ") p2 (getpoint "\nP2 클릭: ") q2 (getpoint "\nQ2 클릭: "))
      (setvar "OSMODE" old-osmode)

      (if (and p1 q1 p2 q2)
        (progn
          (setq idxP1 (util:refpoly-get-nearest-vertex-idx p1 target-pts)
                idxQ1 (util:refpoly-get-nearest-vertex-idx q1 ref-pts)
                idxP2 (util:refpoly-get-nearest-vertex-idx p2 target-pts)
                idxQ2 (util:refpoly-get-nearest-vertex-idx q2 ref-pts))
          (princ (strcat "\n인덱스: P1=" (itoa idxP1) " P2=" (itoa idxP2) " Q1=" (itoa idxQ1) " Q2=" (itoa idxQ2)))

          (if (or (= idxP1 idxP2) (= idxQ1 idxQ2))
            (princ "\n오류: 동일 정점이 선택되어 구간 교체를 진행할 수 없습니다.")
            (progn
              (setq seg-q (util:get-segment-by-list-order ref-pts idxQ1 idxQ2))
              (if (<= idxP1 idxP2)
                (progn
                  (setq idxP1-prev (if (= idxP1 0) (1- (length target-pts)) (1- idxP1))
                        idxP2-next (rem (1+ idxP2) (length target-pts))
                        rem-p (util:get-segment-by-list-order target-pts idxP2-next idxP1-prev)
                        new-pts (append rem-p seg-q))
                )
                (progn
                  (setq cntA (- (length target-pts) idxP1)
                        segA (util:subseq seg-q 0 cntA)
                        segB (util:subseq seg-q cntA (length seg-q))
                        idxP2-next (rem (1+ idxP2) (length target-pts))
                        idxP1-prev (if (= idxP1 0) (1- (length target-pts)) (1- idxP1))
                        rem-p (util:get-segment-by-list-order target-pts idxP2-next idxP1-prev)
                        new-pts (append segB rem-p segA))
                  (princ (strcat "\n래핑 치환: segA=" (itoa (length segA)) " segB=" (itoa (length segB)) " rem=" (itoa (length rem-p))))
                )
              )

              (if (util:pts-same-p target-pts new-pts 0.0001)
                (princ "\n오류: 계산 결과가 원본과 동일합니다. 선택 점을 다시 확인하세요.")
                (progn
                  (util:replace-lwpoly-vertices target-ent new-pts)
                  (if (util:pts-same-p (util:get-vertices target-ent) new-pts 0.0001)
                    (progn (setq changed T) (princ "\n교체 완료."))
                    (princ "\n오류: 좌표 치환이 반영되지 않았습니다.")
                  )
                )
              )
            )
          )
        )
        (princ "\n오류: 점 입력이 완료되지 않아 작업을 중단했습니다.")
      )
    )
  )

  (if (and ok (not changed)) (princ "\n안내: 실행은 완료되었지만 변경된 정점이 없습니다."))
  (setvar "OSMODE" old-osmode)
  (vla-EndUndoMark doc)
  (princ)
)

(defun C:REFPOLY_CW ()
  (fn:refpoly-engine "시계(CW)" 'util:ensure-clockwise 'util:ensure-clockwise)
)

(defun C:REFPOLY_CCW ()
  (fn:refpoly-engine "CCW" 'util:ensure-clockwise 'util:ensure-counter-clockwise)
)

(princ "\n폴리라인 방향 정렬 로드 완료.")
(princ)
