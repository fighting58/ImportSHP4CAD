;;; MapIndex.lsp
;;; 원점(200000, 600000) 기준 축척별 도곽 자동 생성 도구
;;; AutoCAD 2013 & Windows 11 환경 최적화

(vl-load-com)

;; util:floor 함수 (내림)
(defun util:floor (n)
  (if (>= n 0)
    (fix n)
    (if (= n (float (fix n)))
      (fix n)
      (fix (1- n))
    )
  )
)


;; ==========================================
;; [1] 유틸리티 함수
;; ==========================================

;; 레이어 생성 및 색상 설정
(defun util:ensure-layer (layname color)
  (if (not (tblsearch "LAYER" layname))
    (command "_.layer" "_make" layname "_color" color layname "_ltype" "Continuous" layname "")
  )
)

;; 객체들의 통합 범위(Bounding Box) 계산
(defun util:get-extent (ss / i ent obj minpt maxpt all-min all-max)
  (setq i 0)
  (repeat (sslength ss)
    (setq ent (ssname ss i))
    (setq obj (vlax-ename->vla-object ent))
    (vla-getboundingbox obj 'minpt 'maxpt)
    (setq minpt (vlax-safearray->list minpt)
          maxpt (vlax-safearray->list maxpt))
    
    (if (not all-min)
      (setq all-min minpt all-max maxpt)
      (setq all-min (list (min (car all-min) (car minpt)) (min (cadr all-min) (cadr minpt)))
            all-max (list (max (car all-max) (car maxpt)) (max (cadr all-max) (cadr maxpt))))
    )
    (setq i (1+ i))
  )
  (list all-min all-max)
)

;; 사각형 도곽 그리기 (LWPOLYLINE)
(defun util:draw-rect (p1 p2 layname)
  (entmake 
    (list 
      '(0 . "LWPOLYLINE")
      '(100 . "AcDbEntity")
      (cons 8 layname)
      '(100 . "AcDbPolyline")
      '(90 . 4)
      '(70 . 1)
      (cons 10 (list (car p1) (cadr p1)))
      (cons 10 (list (car p2) (cadr p1)))
      (cons 10 (list (car p2) (cadr p2)))
      (cons 10 (list (car p1) (cadr p2)))
    )
  )
)

;; ==========================================
;; [2] 핵심 연산 엔진
;; ==========================================

(defun fn:create-map-index (scale / *error* doc origin-x origin-y cell-w cell-h
                                     mode p1 p2 ss extent min-x min-y max-x max-y
                                     start-col end-col start-row end-row
                                     cur-x cur-y count i j found-in-cell cell-ss k ok)
  (setq doc (vla-get-ActiveDocument (vlax-get-acad-object)))

  (defun *error* (msg)
    (if (not (member msg '("Function cancelled" "quit / exit abort")))
      (princ (strcat "\n오류: " msg))
    )
    (vla-EndUndoMark doc)
    (princ)
  )

  (vla-StartUndoMark doc)
  (setq ok T)

  ;; 원점 (단위: m)
  (setq origin-x 200000.0
        origin-y 600000.0)

  (if (= scale 500)
    (setq cell-w 200.0 cell-h 150.0) ; 1:500 (400mm * 0.5 = 200m)
    (setq cell-w 400.0 cell-h 300.0) ; 1:1000 (400mm * 1.0 = 400m)
  )

  ;; 1. 입력 영역 설정 (윈도우 또는 객체 선택)
  (initget "Object Window")
  (setq mode (getkword "\n도곽 생성 방식 선택 [객체(O)/윈도우(W)] <윈도우>: "))
  (if (or (= mode "") (not mode)) (setq mode "Window"))

  (if (= mode "Window")
    (progn
      (princ "\n--- 윈도우(W) 모드 실행 ---")
      (setq p1 (getpoint "\n첫 번째 구석 클릭: "))
      (setq p2 (getcorner p1 "\n반대쪽 구석 클릭: "))
      (setq min-x (min (car p1) (car p2))
            min-y (min (cadr p1) (cadr p2))
            max-x (max (car p1) (car p2))
            max-y (max (cadr p1) (cadr p2)))
    )
    (progn
      (princ "\n--- 객체 선택(O) 모드 (포함된 칸만 생성) ---")
      (princ "\n도곽 영역을 계산할 객체들을 선택하고 엔터를 누르세요...")
      (setq ss (ssget))
      (if (not ss)
        (progn
          (princ "\n선택된 객체가 없어 중단합니다.")
          (setq ok nil)
        )
      )

      (if ok
        (progn
          (setq extent (util:get-extent ss))
          (setq min-x (car (car extent))
                min-y (cadr (car extent))
                max-x (car (cadr extent))
                max-y (cadr (cadr extent)))
        )
      )
    )
  )

  (if ok
    (progn
      ;; 2. 그리드 인덱스 계산
      (setq start-col (fix (util:floor (/ (- min-x origin-x) cell-w)))
            end-col   (fix (util:floor (/ (- max-x origin-x) cell-w)))
            start-row (fix (util:floor (/ (- min-y origin-y) cell-h)))
            end-row   (fix (util:floor (/ (- max-y origin-y) cell-h))))

      ;; 3. 도곽 생성
      (util:ensure-layer "dokwag" 1) ; Red
      (setq count 0)
      (setq i start-col)
      (while (<= i end-col)
        (setq j start-row)
        (while (<= j end-row)
          (setq cur-x (+ origin-x (* i cell-w))
                cur-y (+ origin-y (* j cell-h)))

          (setq p1 (list cur-x cur-y)
                p2 (list (+ cur-x cell-w) (+ cur-y cell-h)))

          (setq found-in-cell nil)
          (if (= mode "Window")
            (setq found-in-cell T) ; 윈도우 모드는 전체 생성
            (progn
              ;; 객체 선택 모드: 현재 칸(p1, p2)에 초기 선택 객체(ss)가 포함되어 있는지 확인
              (setq cell-ss (ssget "_C" p1 p2))
              (if cell-ss
                (progn
                  (setq k 0)
                  (repeat (sslength cell-ss)
                    (if (not found-in-cell)
                      (if (ssmemb (ssname cell-ss k) ss)
                        (setq found-in-cell T)
                      )
                    )
                    (setq k (1+ k))
                  )
                )
              )
            )
          )

          ;; 교차하는 객체가 있거나 윈도우 모드인 경우에만 생성
          (if found-in-cell
            (progn
              (util:draw-rect p1 p2 "dokwag")
              (setq count (1+ count))
            )
          )
          (setq j (1+ j))
        )
        (setq i (1+ i))
      )
    )
  )

  (vla-EndUndoMark doc)
  (if ok
    (princ (strcat "\n완료: " (itoa count) "개의 도곽이 'dokwag' 레이어에 생성되었습니다."))
  )
  (princ)
)
;; ==========================================
;; [3] 메인 명령어
;; ==========================================

(defun C:MAPINDEX_500 () (fn:create-map-index 500))
(defun C:MAPINDEX_1000 () (fn:create-map-index 1000))

(princ "\n도곽 생성 도구 로드 완료.")
(princ)

