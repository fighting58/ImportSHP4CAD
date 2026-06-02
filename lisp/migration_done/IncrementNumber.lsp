;;; IncrementNumber.lsp
;;; AutoCAD 자동 증가 숫자 입력 도구
;;; Optimized for Windows 11 / AutoCAD 2013 (No Dialogs)

(vl-load-com)

;;; [전역 변수 설정]
(if (not *INCNUM-SIZE*)    (setq *INCNUM-SIZE* 1.0))
(if (not *INCNUM-PREFIX*)  (setq *INCNUM-PREFIX* ""))
(if (not *INCNUM-POSTFIX*) (setq *INCNUM-POSTFIX* ""))

(defun C:INCNUM (/ *error* old-cmdecho size prefix postfix start
                  num pt txt-val cur-size kw continue-settings
                  aborted)

  ;; [1] 에러 핸들러 (Esc/오류 발생 시 즉시 복구)
  (defun *error* (msg)
    (if old-cmdecho (setvar "CMDECHO" old-cmdecho))
    (if (member msg '("Function cancelled" "quit / exit abort"))
      (princ "\n사용자에 의해 취소되었습니다.")
      (princ (strcat "\n오류: " msg))
    )
    (setq aborted T)
    (princ)
  )

  ;; [2] 환경 설정 백업 및 초기화
  (setq old-cmdecho (getvar "CMDECHO"))
  (setvar "CMDECHO" 0)
  (setq aborted nil)

  ;; [3] 현재 세션의 설정값 불러오기
  (setq size *INCNUM-SIZE*)
  (setq prefix *INCNUM-PREFIX*)
  (setq postfix *INCNUM-POSTFIX*)

  ;; [4] 설정 변경 루프 (컴팩트 모드: 2행으로 표시)
  (setq continue-settings T)
  (while continue-settings
    (initget "Size Prefix pOstfix")
    ;; 1행: 현재 설정 상태 표시, 2행: 사용자 입력 요청 (총 2행)
    (setq kw (getkword (strcat "\n[현재설정 - 크기: " (rtos size 2 2) ", 접두사: \"" prefix "\", 접미사: \"" postfix "\"]"
                               "\n설정변경 (Size/Prefix/pOstfix) [Enter: 시작값 입력]: ")))

    (cond
      ((= kw "Size")
       (initget 6)
       (setq cur-size (getdist (strcat "\n변경할 폰트 크기 입력 (" (rtos size 2 2) "): ")))
       (if cur-size (setq size cur-size))
      )
      ((= kw "Prefix")
       (setq prefix (getstring T (strcat "\n변경할 접두사 입력 (현재: \"" prefix "\") [Enter: 삭제]: ")))
      )
      ((= kw "pOstfix")
       (setq postfix (getstring T (strcat "\n변경할 접미사 입력 (현재: \"" postfix "\") [Enter: 삭제]: ")))
      )
      (T (setq continue-settings nil))
    )

    ;; 변경된 설정을 전역 변수에 실시간 저장
    (setq *INCNUM-SIZE* size)
    (setq *INCNUM-PREFIX* prefix)
    (setq *INCNUM-POSTFIX* postfix)
  )

  ;; [5] 시작값 입력 (Enter 시 1로 시작, Esc 시 중단)
  (initget 4)
  (setq start (getint "\n시작값을 입력하세요 (1) [Esc: 취소]: "))
  (if (not start) (setq start 1))
  (setq num start)

  ;; [6] 메인 입력 루프 (Enter/Esc 시 종료)
  (princ "\n화면 클릭 시 숫자 삽입. 종료하려면 Enter 또는 Esc.")
  (while (setq pt (getpoint "\n삽입 지점 클릭: "))
    (setq txt-val (strcat prefix (itoa num) postfix))

    (entmake (list
               '(0 . "TEXT")
               (cons 10 pt)
               (cons 40 size)
               (cons 1 txt-val)
               '(7 . "Standard")
               '(72 . 1)
               '(73 . 2)
               (cons 11 pt)
             ))

    (setq num (1+ num))
    (princ (strcat "\n삽입됨: " txt-val ". 다음 숫자: " (itoa num)))
  )

  ;; [7] 환경 설정 복구 및 종료
  (setvar "CMDECHO" old-cmdecho)
  (if (not aborted)
    (princ "\n정상적으로 종료되었습니다.")
  )
  (princ)
)
(princ "\n자동 숫자 증가 도구 로드 완료.")
(princ)

