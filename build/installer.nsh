!include LogicLib.nsh
!include FileFunc.nsh
!include WinMessages.nsh

; A launch-only copy of Setup runs the shell operation outside the visible UI.
; It exits in preInit, before installation, elevation selection or the setup mutex.
!macro preInit
 !ifndef BUILD_UNINSTALLER
  ${GetParameters} $R0
  ClearErrors
  ${GetOptions} $R0 "/OWL_LAUNCH_APP=" $R1
  ${IfNot} ${Errors}
   ${GetOptions} $R0 "/OWL_LAUNCH_RESULT=" $R2
   ${If} $R1 != ""
   ${AndIf} $R2 != ""
    StrCpy $R3 $R2
    ${StdUtils.ExecShellAsUser} $R4 "$R1" "open" ""
    WriteINIStr "$R3" "Launch" "Result" "$R4"
   ${EndIf}
   Quit
  ${EndIf}
 !endif
!macroend

!macro customFinishPage
 Var owlLaunchState
 Var owlLaunchProcess
 Var owlLaunchTicks
 Var owlLaunchResult

 !define MUI_FINISHPAGE_RUN
 !define MUI_FINISHPAGE_RUN_TEXT "Run Owl AI after Finish"
 !define MUI_FINISHPAGE_RUN_FUNCTION OwlFinishNoop
 !define MUI_PAGE_CUSTOMFUNCTION_LEAVE OwlFinishLeave
 !insertmacro MUI_PAGE_FINISH

 Function OwlFinishNoop
 FunctionEnd

 Function OwlFinishLeave
  ${If} $owlLaunchState == "done"
   Return
  ${EndIf}
  ${If} $owlLaunchState == "opening"
   Abort
  ${EndIf}
  SendMessage $mui.FinishPage.Run ${BM_GETCHECK} 0 0 $0
  ${If} $0 != ${BST_CHECKED}
   Return
  ${EndIf}

  StrCpy $owlLaunchState "opening"
  GetDlgItem $0 $HWNDPARENT 1
  EnableWindow $0 0
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Opening Owl AI..."
  EnableWindow $mui.FinishPage.Run 0
  SendMessage $mui.FinishPage.Text ${WM_SETTEXT} 0 "STR:Opening Owl AI...$\r$\nPlease wait. You only need to click Finish once."
  System::Call 'user32::UpdateWindow(p $HWNDPARENT)'
  InitPluginsDir
  StrCpy $owlLaunchResult "$PLUGINSDIR\owl-launch.ini"
  Delete "$owlLaunchResult"
  StrCpy $owlLaunchTicks 0

  ; NSIS uses a 32-bit process: STARTUPINFOW=68, PROCESS_INFORMATION=16.
  ; CreateProcess returns immediately; no ShellExecute waits in the UI thread.
  System::Call '*(i 68,p 0,p 0,p 0,i 0,i 0,i 0,i 0,i 0,i 0,i 0,i 0,i 0,p 0,p 0,p 0,p 0)p.r2'
  System::Alloc 16
  Pop $3
  StrCpy $4 '$\"$EXEPATH$\" /S /OWL_LAUNCH_APP=$\"$appExe$\" /OWL_LAUNCH_RESULT=$\"$owlLaunchResult$\"'
  System::Call 'kernel32::CreateProcessW(w "$EXEPATH",w r4,p 0,p 0,i 0,i 0,p 0,p 0,p r2,p r3)i.r5'
  ${If} $5 != 0
   System::Call '*$3(p .r6,p .r7,i,i)'
   StrCpy $owlLaunchProcess $6
   System::Call 'kernel32::CloseHandle(p r7)'
  ${EndIf}
  System::Free $2
  System::Free $3
  ${If} $5 == 0
   Call OwlLaunchFailed
  ${Else}
   ${NSD_CreateTimer} OwlLaunchPoll 200
  ${EndIf}
  Abort
 FunctionEnd

 Function OwlLaunchPoll
  IntOp $owlLaunchTicks $owlLaunchTicks + 1
  System::Call 'kernel32::GetExitCodeProcess(p $owlLaunchProcess,*i .r0)i.r1'
  ${If} $1 == 0
   StrCpy $0 1
  ${EndIf}
  ${If} $0 == 259
   ${If} $owlLaunchTicks < 100
    Return
   ${EndIf}
   ; Only terminate the launch helper we created, never Owl AI or Explorer.
   System::Call 'kernel32::TerminateProcess(p $owlLaunchProcess,i 1)'
  ${EndIf}
  ${NSD_KillTimer} OwlLaunchPoll
  System::Call 'kernel32::CloseHandle(p $owlLaunchProcess)'
  StrCpy $owlLaunchProcess ""
  ReadINIStr $0 "$owlLaunchResult" "Launch" "Result"
  ${If} $0 == "ok"
  ${OrIf} $0 == "fallback"
   StrCpy $owlLaunchState "done"
   SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_UNCHECKED} 0
   GetDlgItem $0 $HWNDPARENT 1
   EnableWindow $0 1
   System::Call 'user32::PostMessageW(p $HWNDPARENT,i ${WM_COMMAND},p 1,p 0)'
  ${Else}
   Call OwlLaunchFailed
  ${EndIf}
 FunctionEnd

 Function OwlLaunchFailed
  StrCpy $owlLaunchState "done"
  SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_UNCHECKED} 0
  SendMessage $mui.FinishPage.Text ${WM_SETTEXT} 0 "STR:Owl AI was installed, but Windows did not confirm its launch.$\r$\nClick Finish to close Setup, then open Owl AI from your desktop or Start menu."
  GetDlgItem $0 $HWNDPARENT 1
  SendMessage $0 ${WM_SETTEXT} 0 "STR:Finish"
  EnableWindow $0 1
 FunctionEnd
!macroend
