# Development helper. Receives only public UI captions, never business data.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$bingSession = $null
while ($requestLine = [Console]::ReadLine()) {
    try {
        $request = $requestLine | ConvertFrom-Json
        if ($request.language -in @('brx', 'ks')) {
            if (-not $bingSession) {
                $html = (Invoke-WebRequest -Uri 'https://www.bing.com/translator' -UseBasicParsing -TimeoutSec 30).Content
                $helper = [regex]::Match($html, 'params_AbusePreventionHelper\s*=\s*(\[[^;]+\])').Groups[1].Value | ConvertFrom-Json
                $bingSession = @{
                    token = $helper[1]; key = $helper[0]
                    ig = [regex]::Match($html, 'IG:"([^"]+)"').Groups[1].Value
                    iid = [regex]::Match($html, 'data-iid="([^"]+)"').Groups[1].Value
                }
            }
            $uri = 'https://www.bing.com/ttranslatev3?isVertical=1&IG=' + $bingSession.ig + '&IID=' + $bingSession.iid
            $reply = Invoke-RestMethod -Method Post -Uri $uri -Body @{
                fromLang = 'en'; text = $request.text; to = $request.language
                token = $bingSession.token; key = $bingSession.key
            } -TimeoutSec 30
            if (-not $reply -or -not $reply[0].translations) {
                throw ('Translator returned no text: ' + ($reply | ConvertTo-Json -Compress -Depth 4))
            }
            $text = $reply[0].translations[0].text
        } else {
            $uri = 'https://translate.googleapis.com/translate_a/single?client=gtx&sl=en&dt=t&tl=' +
                $request.language + '&q=' + [Uri]::EscapeDataString($request.text)
            $reply = Invoke-RestMethod -Uri $uri -TimeoutSec 30
            $text = ($reply[0] | ForEach-Object { $_[0] }) -join ''
        }
        if (-not $text) { throw 'No translation returned.' }
        [Console]::WriteLine((@{text = $text} | ConvertTo-Json -Compress))
    } catch {
        [Console]::WriteLine((@{error = $_.Exception.Message} | ConvertTo-Json -Compress))
    }
}
