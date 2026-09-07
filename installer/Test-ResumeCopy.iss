; Non-installing regression harness: exits from InitializeSetup, before any install.
[Setup]
AppName=AudioZen Resume Copy Test
AppVersion=1.0
DefaultDirName={tmp}\AudioZenResumeTest
PrivilegesRequired=lowest
Uninstallable=no
CreateAppDir=no
OutputDir=..\artifacts\tests
OutputBaseFilename=ResumeCopyTest
SetupLogging=yes
[Code]
#include "ResumeCopy.iss"

function InitializeSetup: Boolean;
var
  SourcePath, DestinationPath, MessageText: String;
begin
  Result := False;
  SourcePath := ExpandConstant('{srcexe}');
  DestinationPath := ExpandConstant('{tmp}\resume-test.exe');
  MessageText := CopyResumeInstaller(SourcePath, DestinationPath);
  if MessageText <> '' then RaiseException(MessageText);
  if GetSHA256OfFile(SourcePath) <> GetSHA256OfFile(DestinationPath) then
    RaiseException('Copied executable hash mismatch');
  Log('PASS: running installer copied before installation and SHA-256 matches');
  MessageText := CopyResumeInstaller(SourcePath, DestinationPath);
  if MessageText = '' then RaiseException('Existing destination must not be overwritten');
  if Pos('Windows', MessageText) = 0 then RaiseException('Missing error diagnostics');
  Log('PASS: existing destination rejected with Windows error diagnostics');
  DeleteFile(DestinationPath);
end;
