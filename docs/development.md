# 開發與發佈

## 環境

- .NET 8 SDK（要含 Windows Desktop workload，`dotnet --list-runtimes` 要看得到 `Microsoft.WindowsDesktop.App`）
- Windows 10/11
- 沒有任何 NuGet 套件依賴，clone 下來直接 build

```powershell
dotnet build                       # Debug
dotnet run --project TwMarketWidget.csproj
dotnet build -c Release
```

或用 `build.ps1`（會先檢查 SDK、`dotnet` 不在 PATH 時會自己去標準路徑找）：

```powershell
powershell -ExecutionPolicy Bypass -File .\build.ps1 -Run -Shortcut
.\build.ps1 -Publish               # 產生 dist\，免安裝 Runtime
```

> **改 `build.ps1` 時注意**：檔案裡有中文，必須存成 **UTF-8 with BOM**。
> Windows PowerShell 5.1 會把沒有 BOM 的 `.ps1` 當成系統 ANSI 編碼讀，中文字串跟註解會變亂碼並直接噴語法錯誤。

## 診斷模式

```powershell
.\bin\Release\net8.0-windows\TwMarketWidget.exe --selftest
```

不開視窗，直接照設定檔的自選清單打一次所有 API，把結果寫到 `%TEMP%\TwMarketWidget-selftest.txt`：

```
t00            加權指數      last=44450.34 chg=-161.26 open=44487.94 … t=10:36:45
TX     TXFH6   台指期近月    last=44308.00 chg=-229.00  … vol=32434    t=10:37:01
取得 7/7 檔
分時 TX      113 點 08:46~10:38 首 44207.00 末 44308.00
分時 o00    無資料
```

會一併印出期貨解析到的合約（`TXFH6`）與每檔分時底線的點數與時間範圍。
**資料源改版時先跑這個**，對照 [data-sources.md](data-sources.md) 的欄位表就知道哪個欄位不見了。

未預期的例外寫在 `%TEMP%\TwMarketWidget-crash.txt`。無邊框視窗出事時不會有任何提示，只會消失，所以有這個檔。

## 改 UI 時怎麼驗

`--selftest` 只驗得到資料層。版面問題（欄寬被截掉、白底白字、控制項沒渲染）只有真的把視窗叫起來才看得出來：
把視窗跑起來、抓 `GetWindowRect` 的範圍截圖來看。用 `RenderTargetBitmap` 對沒有 `Show()` 過的 `Window`
離螢幕算圖是行不通的——內容畫得出來，但 binding 不會求值，表格會是空的，看不出真正的問題。

實際靠這個抓到過的問題：數字欄位太窄把 `44611.60` 截成 `4611.60`、
半透明視窗下 `ComboBox` 變白底白字看不到選項。

## 發佈

推一個 `v` 開頭的 tag 就會跑 `.github/workflows/release.yml`：

```powershell
git tag v1.0.0
git push origin v1.0.0
```

| 產出 | 大小 | 需求 |
| --- | --- | --- |
| `TwMarketWidget-<版本>-win-x64-framework-dependent.zip` | ~0.3 MB | 要先裝 .NET 8 Desktop Runtime |
| `TwMarketWidget-<版本>-win-x64-self-contained.zip` | ~65 MB | 解壓縮就能跑 |

流程分兩個 job：`build` 用 matrix 同時打包兩種組態上傳 artifact，`release` 等兩個都好之後用
`gh release create/upload` 開一次 Release——避免兩個平行 job 搶著建同一個 Release。
用內建的 `github.token`，不需要額外設 secret，也沒有用第三方 action。

也可以在 Actions 頁面手動觸發（`workflow_dispatch`），那種情況只產 artifact、不建 Release。

### 版本號

tag 會透過 `-p:Version=` 寫進組件版本，`v1.2.3` → `1.2.3`。手動觸發時用輸入的版本號，沒填就是 `0.0.0`。

### 打包細節

```
dotnet publish -c Release -r win-x64 --self-contained <bool>
  -p:PublishSingleFile=true -p:DebugType=embedded
```

- `PublishSingleFile` 對 WPF 有效，但 self-contained 會另外留下 5 個原生 DLL
  （`wpfgfx_cor3.dll`、`PresentationNative_cor3.dll` 等）沒辦法併進去，zip 裡會看到它們，是正常的。
- WPF **不支援 trimming**，self-contained 的體積壓不下來。
- `DebugType=embedded`：符號嵌在 exe 裡，沒有另外的 pdb，crash log 仍然有行號。

改 workflow 之前先在本機把同一組 publish 指令跑過、並用打包出來的 exe 跑一次 `--selftest`，
比推了 tag 才發現壞掉省事。
