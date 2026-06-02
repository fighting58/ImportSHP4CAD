;;; ParcelSplit.lsp
;;; 지번 레이어(19)를 구성 요소별로 분해하여 10, 11, 20 레이어로 분리 생성
;;; AutoCAD 2013 & Windows 11 환경 최적화

(vl-load-com)

;; ==========================================
;; [1] 유틸리티 함수
;; ==========================================

;; 레이어 생성 및 색상 설정
(defun util:ensure-layer (layname color / old-layer)
  (setq old-layer (getvar "CLAYER"))
  (if (not (tblsearch "LAYER" layname))
    (command "_.layer" "_make" layname "_color" color layname "")
  )
  (if (and old-layer (/= (getvar "CLAYER") old-layer))
    (setvar "CLAYER" old-layer)
  )
)

;; 문자열 내 모든 공백류(스페이스/탭/개행/캐리지리턴) 제거
(defun util:remove-all-whitespace (str / ch)
  (foreach ch (list (chr 32) (chr 9) (chr 10) (chr 13))
    (while (vl-string-search ch str)
      (setq str (vl-string-subst "" ch str))
    )
  )
  str
)

;; 지목 리스트 정의 (28종)
(setq *jimok-list* '("전" "답" "과" "목" "임" "광" "염" "대" "장" "학" "차" "주" "창" "도" "철" "제" "천" "구" "유" "양" "수" "공" "체" "원" "종" "사" "묘" "잡"))

;; 지번 문자열 파싱 엔진
(defun fn:parse-parcel-string (str / res-reg res-jimok res-jibeon last-char)
  (setq str (util:remove-all-whitespace str))
  
  ;; 1. 대장 구분 (20 레이어용)
  (if (wcmatch str "산*")
    (setq res-reg "2" str (vl-string-left-trim "산" str))
    (setq res-reg "1")
  )
  
  ;; 2. 지목 추출 (11 레이어용)
  (setq last-char (substr str (strlen str)))
  (if (member last-char *jimok-list*)
    (progn
      (setq res-jimok last-char)
      (setq str (vl-string-right-trim " " (substr str 1 (1- (strlen str)))))
    )
    (setq res-jimok "")
  )
  
  ;; 3. 지번 정제 (10 레이어용)
  (setq res-jibeon (vl-string-trim " " str))
  ;; 부번이 -0 이거나 - 인 경우 제거
  (cond
    ((wcmatch res-jibeon "*-0") (setq res-jibeon (substr res-jibeon 1 (- (strlen res-jibeon) 2))))
    ((wcmatch res-jibeon "*-") (setq res-jibeon (substr res-jibeon 1 (- (strlen res-jibeon) 1))))
  )
  
  ;; 임야대장(2)인 경우 지번 앞에 "산" 다시 붙이기
  (if (= res-reg "2")
    (setq res-jibeon (strcat "산" res-jibeon))
  )
  
  (list res-jibeon res-jimok res-reg)
)

;; ==========================================
;; [2] 메인 명령어
;; ==========================================

(defun C:PARCEL_SPLIT (/ *error* doc lay-in ss i ent obj str str-data p-jibeon p-jimok p-reg count new-obj item ok)
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

  ;; 1. 레이어 입력 및 준비
  (setq lay-in (getstring "\n지번 레이어 입력(기본: 19): "))
  (if (= lay-in "") (setq lay-in "19"))

  (util:ensure-layer "10" 4) ; Cyan
  (util:ensure-layer "11" 4) ; Cyan
  (util:ensure-layer "20" 3) ; Green

  ;; 2. 객체 선택
  (princ (strcat "\n'" lay-in "' 레이어의 지번 문자들을 처리 중..."))
  (setq ss (ssget "X" (list '(0 . "TEXT,MTEXT") (cons 8 lay-in))))

  (if (not ss)
    (progn
      (princ (strcat "\n오류: '" lay-in "' 레이어에 문자 객체가 없습니다."))
      (setq ok nil)
    )
  )

  ;; 3. 루프 처리
  (if ok
    (progn
      (setq i 0 count 0)
      (repeat (sslength ss)
        (setq ent (ssname ss i))
        (setq obj (vlax-ename->vla-object ent))
        (setq str (vla-get-TextString obj))

        ;; 데이터 파싱
        (setq str-data (fn:parse-parcel-string str))
        (setq p-jibeon (nth 0 str-data)
              p-jimok   (nth 1 str-data)
              p-reg     (nth 2 str-data))

        ;; 새로운 문자 객체 생성 (동일 위치/속성 복제)
        (foreach item (list (list "10" p-jibeon) (list "11" p-jimok) (list "20" p-reg))
          (if (and (cadr item) (/= (cadr item) ""))
            (progn
              (setq new-obj (vla-copy obj))
              (vla-put-Layer new-obj (car item))
              (vla-put-TextString new-obj (cadr item))
            )
          )
        )

        (setq i (1+ i) count (1+ count))
      )
    )
  )

  (vla-EndUndoMark doc)
  (if ok
    (princ (strcat "\n완료: 총 " (itoa count) "개의 지번 문자를 분해하였습니다."))
  )
  (princ)
)
(princ "\n지번 레이어 분해 도구 로드 완료.")
(princ)
