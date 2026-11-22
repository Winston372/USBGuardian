## V3.5.2 RC
- 修正通知內文不支援捲動、過早截斷及查看詳情定位不準確。
- 調整通知標題／內文字級與尺寸，滑鼠停留時暫停自動關閉。
- 活動紀錄詳細文字可捲動，加入隱藏在設定中的長訊息測試入口。
- 安全核心未改動；未在 Windows 實機驗收。

## V3.5.1 RC
- 裝置操作改成清楚的「選裝置 → 看狀態 → 設提醒／停用儲存介面」；復原和其他管理功能收合。
- 已標記與已停用的狀態分開呈現，不宣稱提供永久或插入前系統封鎖。
- 連續曲率裝置面板及文字輸入框的 UI 風格調整，新增中英文介面文字。
- 核心安全模組未更動；Windows 實機測試未完成。

# V3.5.0 RC — UI 操作更新（2026-11）

- 新增獨立「裝置操作」分頁，保留原信任標記、停用／復原與匯出功能。
- 首頁事件可點擊定位到技術日誌；提醒提供逐筆詳情。
- 通知定位右下角，沿用原有三級提示及圓角設計。
- 保留 SafeLab、Defender、隔離檔案、首次使用確認及多語系。
- 修正舊版 XAML 的 x:String 問題，改用 sys:String。
- 此版本尚未在 Windows 上編譯／實機測試，需執行附帶驗收腳本。

# USB Guardian changes

## V3.4.4 RC — UI microcopy and usability cleanup (2026-11-19)

- Reduced generic/repetitive explanatory copy; kept warnings for operations that can affect files or device state.
- Consistent terminology for **preview**, **Defender scan**, and **quarantine**.
- Moved test-notification buttons into a collapsed Settings section.
- Removed repeated headings/empty decorative footer, kept all controls and event handlers.
- Paired Traditional Chinese and English UI strings (144 keys), source regression checks and contrast tests.
- Version metadata updated; critical security logic is unchanged.
- Source tests passed; Windows WPF GUI build and hands-on user testing still required.

---

## V3.3 RC — 純視覺改版

- 主程式重新整理成簡潔 Windows desktop 工具風格，新增一致的左側向量圖示和選取狀態。
- 主題改為中性灰藍色、改善對比和深淺色互動狀態。
- 縮小無效空白，限制閱讀寬度，統一清單、按鈕與通知視覺語言。
- 既有事件與功能均不改寫，首次使用條款未更動。
- 未經 Windows 編譯或實際操作驗證，不標記為正式發布。

# USB Guardian changes

## V3.2 RC — compact desktop UI, consent and alerts (2026-11-18)

- Desktop history view: two compact side-by-side lists for alerts and quarantined files; log under an Advanced expander.
- First-use notice: full scroll-to-end gate, explicit unchecked consent, bilingual updated risk disclosure, no monitoring before acceptance.
- Redesigned WPF popup with common visual tokens and three *presentation priorities* (information, caution, high-priority). Size changes moderately by priority; risk is never determined from filenames alone.
- Persistent history entry for alerts; high-priority popup is not replaced by non-critical updates. Never auto-dismiss the highest priority toast.
- Settings: visual previews for information/caution/high priority, clearly identified as tests with no security side effects.
- Dark and light theme semantic colors for each priority. No new USB blocking claims or unverified antivirus results.
- Source-only cross-platform static checks pass; **no Windows runtime / .NET build verification in this environment**.

# V3.1 RC — usability refinement (2026-11-18)

- Filter USB picker to removable and Windows-confirmed USB-bus volumes; exclude internal/system volumes.
- New empty-state and disabled actions when no USB is connected.
- Clear stale file and preview results when switching or removing a drive.
- Avoid showing scan outcomes for the wrong selected drive.
- Compact Windows Fluent-inspired navigation, typography, status strip and file toolbar.
- Core security modules unchanged; Windows runtime/build verification still required.

---

# Changelog

## 3.0.0-rc1 — Recoverable response and user-first validation

- Replaced direct preview-page permanent deletion with reversible **AES-GCM / Windows DPAPI** quarantine for explicitly selected `Removable` drive files (24 MiB cap), enforcing review-time SHA-256 match and rechecking file attributes before removal.
- Added History > Quarantined files with restore, explicit permanent removal and refresh. Nothing is automatically quarantined.
- Added persistent local alert centre (up to 150 entries), review and mark-read actions with repeated-notification deduplication. Session technical logs are collapsed by default.
- Added on-demand unique drive-letter -> partition -> USBSTOR `DiskDrive` matching; refuses direct device selection if ambiguous.
- Added preflight PnP class check and post-action `DEVPKEY_Device_ProblemCode` verification. Only code 22 is reported as verified disabled; unverifiable outcomes keep a recovery record.
- Clearer Defender status: Windows Security must be consulted for malware verdict; a finished command is not a clean result.
- Updated mandatory first-use notices and bilingual resource keys; consent version bumped to V3.
- Added cross-platform static validation, Windows-only build check, GitHub Actions compilation workflow, novice persona walkthrough and professional security risk register.

**Release status:** source release candidate. Cross-platform source checks passed; Windows/.NET build, GUI, sandbox security controls, device-control tests and actual end-user testing are pending. Do not distribute as a verified anti-malware or pre-insertion blocking product.

# Changelog

## 2.0 — User-first UI refactor (source release)
- Consolidated file inspector and SafeLab preview into **Check USB**; moved image/text preview and format information ahead of SHA/HEX.
- Replaced technical dashboard counters with a human-readable **Home** status and two primary actions.
- Consolidated left navigation to Home, Check USB, Devices, History and Settings.
- Reduced default clutter in Devices; additional filters and manual USBSTOR controls are collapsed by default.
- Added live light/dark/system appearance resources and localized core UI strings in zh-TW and English.
- Restyled bottom-left toasts with shared app color resources; converted resource-usage info to a nonblocking themed toast.
- Added English first-use notice selected by UI culture; consent version bumped.
- Retained existing SafeLab AppContainer, Defender and manual device control logic.

This is untested source in the current environment; Windows build, theme, localization, SafeLab and driver controls need acceptance testing before publication.
