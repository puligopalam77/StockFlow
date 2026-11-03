# Android app, camera and microphone

Use the same hosted StockFlow application through its browser URL or Android app. Both use the same shop records and permissions; each has its own sign-in session. A network connection is required to save transactions.

Device draft recovery is not offline finalized billing or a synchronized invoice
queue. Browser/Android printing and scan inputs exist, but physical camera/USB/
Bluetooth scanners and A4/80mm/58mm printers still need acceptance on the actual
devices. Save as PDF does not prove a physical receipt was printed.

## Ask supported questions

1. Open **Ask StockBot Assist**. Type a question, use the microphone or choose a suggestion for the current screen. How-to answers use approved guides; live figures use your permitted shop records.
2. Ask **How much sales today?**, **How many units sold yesterday?**, **How many invoices last week?** or **How much amount collected today?**. Name the period; sales value and money collected are different figures.
3. Other supported examples include **Customer dues**, **Supplier balance for Supplier One**, **How much UPI collected today?**, **How many returns today?**, **Which purchases are pending delivery?**, **Which products need reordering?**, **Which customers have overdue dues?** and **Which repairs are ready for collection?**. Use the exact product or party name when filtering.
4. Use **Which products sold most this month?**, **Compare this week's sales with last week** or **Warranty expiry for serial SN-123** for supported ranking, comparison and serial questions. Missing dates or names may require a follow-up answer.
5. Common instructions return directly; unfamiliar wording may require the local AI service and can be slower. A service failure is not a zero total. Review the relevant report when the assistant cannot provide a supported figure.
6. Complete profit, historical stock and arbitrary employee/location filters are not supported chat calculations. Use **Product sales & profit** for its documented estimate. The assistant cannot complete transactions or change permissions for you.

## Connect the Android app

1. Open StockFlow and enter the HTTPS shop address supplied by your administrator in **Connect to StockFlow**. Enter the server address only, without a sales-page path, username or password.
2. A phone's **localhost** refers to the phone, not the shop computer. Ask the administrator for a reachable address with a trusted certificate.
3. Sign in using your existing StockFlow account. Signing in to the website does not automatically sign in to the Android app.
4. Before using **Connection** to change the server, save your work and resolve pending transactions. Changing the connection clears the app's local cookies and drafts, but does not delete server records.

## Scan using the camera

1. Beside a supported scan field, select **Camera** and allow camera access.
2. Point at the barcode, review the detected code, then select **Use code**. It enters the code through the same workflow as a scanner or keyboard; it does not automatically save a transaction.
3. Select **Cancel** to stop scanning. Leaving the screen or backgrounding the app stops the camera.
4. If camera permission is denied, enable it in the device's StockFlow app or browser permissions, or type the code manually. Browser camera use requires HTTPS. Camera images are not uploaded for scanning.

## Ask using the microphone

1. Open **Ask StockBot Assist** and select the microphone. Allow microphone access when requested.
2. Speak a short question and select **Stop**. Wait for transcription, review and correct the text, then select **Send**.
3. Use **Cancel** to discard the recording or **Retry** if transcription fails. Recording stops after 60 seconds; shorter recordings are quicker to transcribe.
4. **Speak replies** enables spoken answers, and **Read reply** reads the available reply using supported device voices. Backgrounding stops recording and speech.
5. If transcription is unavailable, type your question and ask the administrator to check the server's speech service. Microphone permission alone does not enable server transcription. HTTPS and device permissions are required.

## Upload files, download documents and print

1. Use the existing **Change logo** or attachment field to select a file. The field's file type and size restrictions still apply; selecting a file alone does not save the form.
2. In the Android app, supported downloads and CSV exports open **Save file**. Choose a destination. Canceling leaves the file unsaved. The app supports downloads up to 20 MB.
3. Use the screen's **Print** action to open browser or Android print controls. Android supports **Save as PDF** and installed compatible print services.
4. Select the appropriate paper for the document, such as A4 or an 80 mm invoice receipt. Physical printers need a compatible print service; direct Bluetooth or USB printer drivers are not included.
5. If connection is interrupted, return to the document and retry its download. For an uncertain sale or stock save, check the recorded result and use its saved-request recovery action before creating another transaction.
