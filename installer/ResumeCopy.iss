// The Inno FileCopy helper cannot copy the running Setup before installation.
// Call Windows directly and capture its error before any other DLL call.
function CopyResumeFile(ExistingFile, NewFile: String; FailIfExists: Boolean): Boolean;
  external 'CopyFileW@kernel32.dll stdcall';

function CopyResumeInstaller(SourcePath, DestinationPath: String): String;
var
  ErrorCode: Integer;
begin
  Result := '';
  if not CopyResumeFile(SourcePath, DestinationPath, True) then begin
    ErrorCode := DLLGetLastError;
    Result := '無法保存安裝程式供重開機後繼續。' + #13#10 +
      '來源：' + SourcePath + #13#10 + '目的地：' + DestinationPath + #13#10 +
      'Windows 錯誤 ' + IntToStr(ErrorCode) + '：' + SysErrorMessage(ErrorCode) + #13#10 +
      '安裝紀錄：' + ExpandConstant('{log}');
    Log(Result);
  end;
end;
