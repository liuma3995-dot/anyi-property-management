' AnYi Property Management System - hidden backend launcher (M8 T8-2-2)
' Purpose: used by the logon autostart entry (HKLM ...\Run\AnYiPropertyServer) to start the
'          local backend without flashing a console window. The server blocks while running
'          when no interactive console is attached (see Program.cs, M8 T8-2-1).
' NOTE: keep this file ASCII-only / no BOM - Windows Script Host does not accept UTF-8 VBS files.
Option Explicit

Dim shell, fso, scriptPath, appDir, serverExe
Set shell = CreateObject("WScript.Shell")
Set fso = CreateObject("Scripting.FileSystemObject")

scriptPath = WScript.ScriptFullName
appDir = fso.GetParentFolderName(scriptPath)
serverExe = fso.BuildPath(appDir, "PropertyManagement.Server.exe")

If Not fso.FileExists(serverExe) Then
    WScript.Quit 2
End If

shell.CurrentDirectory = appDir
' 0 = hidden window, False = do not wait for the process to finish
shell.Run """" & serverExe & """", 0, False
