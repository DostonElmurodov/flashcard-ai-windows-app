Unicode true
RequestExecutionLevel user
Name "Owl AI installer finish test"
OutFile "${TEST_DIR}\finish-${OWL_SCENARIO}.exe"
InstallDir "${TEST_DIR}"
!include MUI2.nsh
!include LogicLib.nsh
!include WinMessages.nsh

; Simulate a slow/failing Windows shell only in this test executable.
!macro TestShell out app verb args
 ReadINIStr $8 "${TEST_DIR}\calls-${OWL_SCENARIO}.ini" "Test" "Count"
 IntOp $8 $8 + 1
 WriteINIStr "${TEST_DIR}\calls-${OWL_SCENARIO}.ini" "Test" "Count" "$8"
 !if "${OWL_SCENARIO}" == "timeout"
  Sleep 30000
 !else
  Sleep 1200
 !endif
 !if "${OWL_SCENARIO}" == "failure"
  StrCpy ${out} "error"
 !else
  StrCpy ${out} "ok"
 !endif
!macroend
!define StdUtils.ExecShellAsUser '!insertmacro TestShell'
!include "..\build\installer.nsh"
Var appExe
Var probeTicks
Var disabledChecks
Var uiErrors
!define MUI_PAGE_CUSTOMFUNCTION_SHOW TestShown
!insertmacro customFinishPage
!insertmacro MUI_LANGUAGE English

Function .onInit
 !insertmacro preInit
 StrCpy $appExe "$EXEPATH"
 StrCpy $probeTicks 0
 StrCpy $disabledChecks 0
 StrCpy $uiErrors 0
FunctionEnd

Function TestShown
 !if "${OWL_SCENARIO}" == "unchecked"
  SendMessage $mui.FinishPage.Run ${BM_SETCHECK} ${BST_UNCHECKED} 0
 !endif
 ${NSD_CreateTimer} TestProbe 100
FunctionEnd

Function TestProbe
 IntOp $probeTicks $probeTicks + 1
 ${If} $probeTicks == 1
  System::Call 'user32::PostMessageW(p $HWNDPARENT,i ${WM_COMMAND},p 1,p 0)'
 ${EndIf}
 ${If} $owlLaunchState == "opening"
  GetDlgItem $0 $HWNDPARENT 1
  System::Call 'user32::IsWindowEnabled(p r0)i.r1'
  ${If} $1 != 0
   IntOp $uiErrors $uiErrors + 1
  ${EndIf}
  ${NSD_GetText} $0 $1
  ${If} $1 != "Opening Owl AI..."
   IntOp $uiErrors $uiErrors + 1
  ${EndIf}
  IntOp $disabledChecks $disabledChecks + 1
  ; Even a queued Finish command must not launch a second worker.
  ${If} $probeTicks == 4
   System::Call 'user32::PostMessageW(p $HWNDPARENT,i ${WM_COMMAND},p 1,p 0)'
  ${EndIf}
 ${EndIf}
 ${If} $owlLaunchState == "done"
  System::Call 'user32::PostMessageW(p $HWNDPARENT,i ${WM_COMMAND},p 1,p 0)'
 ${EndIf}
 ${If} $probeTicks > 260
  StrCpy $uiErrors 999
  Quit
 ${EndIf}
FunctionEnd

Function .onGUIEnd
 WriteINIStr "${TEST_DIR}\result-${OWL_SCENARIO}.ini" "Test" "Ticks" "$probeTicks"
 WriteINIStr "${TEST_DIR}\result-${OWL_SCENARIO}.ini" "Test" "DisabledChecks" "$disabledChecks"
 WriteINIStr "${TEST_DIR}\result-${OWL_SCENARIO}.ini" "Test" "Errors" "$uiErrors"
 WriteINIStr "${TEST_DIR}\result-${OWL_SCENARIO}.ini" "Test" "State" "$owlLaunchState"
FunctionEnd

Section
SectionEnd
