; 波纹音乐 NSIS 安装脚本。构建方式见 docs/release.md。
;
; 用法（三个变量必传，缺了直接编译失败，不会静默产出一个指向空目录的安装包）：
;   makensis /DAPP_VERSION=0.1.0 /DAPP_SOURCE=<publish 输出目录> /DOUTFILE=<输出 exe> installer\BowenMusic.nsi
; 可选（工作流会传绝对路径，本地不传时退回相对本脚本目录的写法）：
;   /DAPP_LICENSE=<LICENSE 绝对路径>  /DAPP_ICON=<图标绝对路径>
;
; ── 三条踩过的坑，改这个文件前先看 ──────────────────────────────────────────
; ★ 本文件必须存成 **UTF-8 with BOM**。makensis 靠 BOM 判断脚本编码，没有 BOM 时
;   按 ANSI 代码页读（日志里显示 "(ACP)"），所有中文会变成乱码 —— 而且是静默的，
;   编译不报错，装完才发现快捷方式名字是乱码。
; ★ SetShellVarContext / SetRegView 这类是**运行期指令**，只能写在 Section 或 Function 里。
;   放在全局作用域会直接编译失败（"not valid outside Section or Function"）。
; ★ **不要用 !cd 去"锚定"相对路径。** NSIS 本来就按脚本所在目录解析相对路径；
;   而 ${__FILEDIR__} 取到的是**调用时给出的那个相对路径**（makensis 在仓库根用
;   installer\BowenMusic.nsi 调用时它就是 "installer"），拿它去 !cd 会拼成
;   installer\installer 然后报错 —— 实测踩过。要绝对路径就让调用方传进来。

Unicode true
SetCompressor /SOLID lzma
SetCompressorDictSize 64

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"

; ── 必传变量 ────────────────────────────────────────────────────────────────
!ifndef APP_VERSION
  !error "缺少 /DAPP_VERSION=<版本号>，例如 /DAPP_VERSION=0.1.0"
!endif
!ifndef APP_SOURCE
  !error "缺少 /DAPP_SOURCE=<publish 输出目录>"
!endif
!ifndef OUTFILE
  !error "缺少 /DOUTFILE=<安装包输出路径>"
!endif

; VIProductVersion 要求四段数字，工作流可以显式传四段的写法覆盖它。
!ifndef APP_VERSION4
  !define APP_VERSION4 "${APP_VERSION}.0"
!endif

; LICENSE 与图标：默认按「相对本脚本目录」写，CI 传绝对路径覆盖。
!ifndef APP_LICENSE
  !define APP_LICENSE "..\LICENSE"
!endif
!ifndef APP_ICON
  !define APP_ICON "..\src\Bodian.WinUI\Assets\Ripple.ico"
!endif

; ── 应用信息 ────────────────────────────────────────────────────────────────
!define APP_NAME      "波纹音乐"
!define APP_EXE       "Bodian.WinUI.exe"
!define APP_ID        "BowenMusic"
!define APP_PUBLISHER "gcnanmu"
!define APP_URL       "https://github.com/ldm0715/bowen_music"
!define UNINST_KEY    "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
!define DATA_DIR      "$LOCALAPPDATA\Bowen"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_ID}"
InstallDirRegKey HKCU "${UNINST_KEY}" "InstallLocation"
RequestExecutionLevel user
BrandingText "${APP_NAME} ${APP_VERSION}"

VIProductVersion "${APP_VERSION4}"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey "FileVersion"     "${APP_VERSION}"
VIAddVersionKey "ProductVersion"  "${APP_VERSION}"
VIAddVersionKey "CompanyName"     "${APP_PUBLISHER}"
VIAddVersionKey "LegalCopyright"  "GPL-3.0"

; ── 界面 ────────────────────────────────────────────────────────────────────
!define MUI_ABORTWARNING
!define MUI_ICON   "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"

!define MUI_WELCOMEPAGE_TITLE "${APP_NAME} ${APP_VERSION}"
!define MUI_WELCOMEPAGE_TEXT "这个向导会安装 ${APP_NAME} —— 非官方波点音乐桌面客户端。$\r$\n$\r$\n本软件以 GPL-3.0 协议开源，与波点音乐官方无任何关联，未获其授权或认可。$\r$\n$\r$\n装好后可以直接用，不需要另外安装 .NET 运行时。"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "${APP_LICENSE}"
!insertmacro MUI_PAGE_COMPONENTS
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES

!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "运行 ${APP_NAME}"
!insertmacro MUI_PAGE_FINISH

!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

; ── 初始化：检查与关闭正在运行的实例 ─────────────────────────────────────────

!macro CloseRunningApp
  ; ★ 用 CSV 输出而不是默认表格：默认输出的「没有匹配任务」提示随系统语言变，
  ;   按它判断会在中文 Windows 上失效。CSV 有匹配时首列就是进程名，与语言无关。
  nsExec::ExecToStack 'cmd /c tasklist /FI "IMAGENAME eq ${APP_EXE}" /FO CSV /NH'
  Pop $0
  Pop $1

  ${StrLoc} $R0 "$1" "${APP_EXE}" ">"

  ${If} $R0 != ""
    MessageBox MB_YESNO|MB_ICONQUESTION \
      "${APP_NAME}正在运行，安装前需要先关闭它。现在关闭吗？" \
      IDYES close IDNO abort

    close:
      nsExec::Exec 'cmd /c taskkill /IM ${APP_EXE} /F'
      Sleep 1000
      Goto done

    abort:
      Abort

    done:
  ${EndIf}
!macroend

; 放在 .onInit 而不是某个页面的 PRE：静默安装（/S）时会跳过所有页面，
; 但 .onInit 一定会执行；而且它跑在欢迎页之前，能更早失败。
Function .onInit
  SetShellVarContext current
  !insertmacro CloseRunningApp
FunctionEnd

Function un.onInit
  SetShellVarContext current
  !insertmacro CloseRunningApp
FunctionEnd

; ── 安装 ────────────────────────────────────────────────────────────────────

Section "${APP_NAME}（必需）" SEC_APP
  SectionIn RO

  SetShellVarContext current
  SetOutPath "$INSTDIR"

  ; /r 会把 Assets、zh-CN、zh-TW 这些子目录一起铺开。
  ; 用 \* 而不是 \*.*：后者会漏掉没有扩展名的文件。
  File /r "${APP_SOURCE}\*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS"
  CreateShortCut "$SMPROGRAMS\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
    SW_SHOWNORMAL "" "${APP_NAME}"

  WriteRegStr HKCU "${UNINST_KEY}" "DisplayName"     "${APP_NAME}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayVersion"  "${APP_VERSION}"
  WriteRegStr HKCU "${UNINST_KEY}" "Publisher"       "${APP_PUBLISHER}"
  WriteRegStr HKCU "${UNINST_KEY}" "DisplayIcon"     "$INSTDIR\Assets\Ripple.ico"
  WriteRegStr HKCU "${UNINST_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINST_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegStr HKCU "${UNINST_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINST_KEY}" "URLInfoAbout"    "${APP_URL}"
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINST_KEY}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "${UNINST_KEY}" "EstimatedSize" "$0"
SectionEnd

Section "创建桌面快捷方式" SEC_DESKTOP
  SetShellVarContext current
  CreateShortCut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}" "" "$INSTDIR\${APP_EXE}" 0 \
    SW_SHOWNORMAL "" "${APP_NAME}"
SectionEnd

Section "Uninstall"
  ; 卸载器运行时是临时目录里的副本，所以 $INSTDIR 下的 Uninstall.exe 没有被占用。
  SetShellVarContext current

  Delete "$SMPROGRAMS\${APP_NAME}.lnk"
  Delete "$DESKTOP\${APP_NAME}.lnk"

  ; 改名前留下的快捷方式。清单只增不减，与 AppIdentity.LegacyShortcutFileNames 保持同步。
  Delete "$SMPROGRAMS\Bodian.lnk"
  Delete "$DESKTOP\Bodian.lnk"

  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "${UNINST_KEY}"

  ; 用户数据默认留着：卸载重装是常见操作，删掉就找不回来了。
  MessageBox MB_YESNO|MB_ICONEXCLAMATION \
    "是否同时删除本机数据？$\r$\n$\r$\n包括登录凭据、播放记录、歌单缓存与全部设置，位于：$\r$\n${DATA_DIR}$\r$\n$\r$\n选择「否」会保留，下次安装后可以继续用。" \
    IDNO keep_data

  RMDir /r "${DATA_DIR}"

  keep_data:
SectionEnd
