# Configuring StockFlow AI help

## Microphone questions

The microphone records up to 60 seconds, then sends a mono 16 kHz WAV through
StockFlow's authenticated, CSRF-protected `/api/help/voice` endpoint to a local
whisper.cpp runtime. Stop transcribes; Cancel discards the recording. Review and
edit the text before selecting Send. Recording does not submit a business question.
Failed transcription can retry the same recording, held only in the current page.
Closing the assistant releases the microphone and discards that recording.

On Windows, install once under the account that runs StockFlow:

```powershell
powershell -File .vscode/Install-StockFlowVoice.ps1
```

Restart StockFlow. It starts the installed CPU runtime on `127.0.0.1:8081` using
the multilingual base model, and stops only a process that it started. The runtime
and model live in `%LOCALAPPDATA%\StockFlowAI\voice`, outside the repository.
This installation is already present on the current development machine.
Deployment to another machine requires its own installation. Optional
`HelpVoice:RuntimePath`, `HelpVoice:ModelPath` and loopback `HelpVoice:Endpoint`
configuration can point to another installed local runtime. Audio is not saved
to the application database or uploaded to a paid speech API.

Browser recording requires microphone permission and HTTPS (localhost works for
development). Accessing the shop from a phone/tablet over plain HTTP on a LAN
does not grant microphone access. Local speech speed and language accuracy depend
on the machine and model; the text remains editable. When recording APIs are
unavailable, the existing browser speech option remains a fallback and may require
internet access. Read reply/Speak replies use the device's installed speech voices.

Protocol reference: [whisper.cpp server](https://github.com/ggml-org/whisper.cpp/blob/master/examples/server/README.md).

### Desktop, tablet and phone access

Every device records in its supported format (WebM, MP4/AAC or Ogg/Opus), converts
it to the same WAV format and calls the same relative `/api/help/voice` URL.
Only the StockFlow application server needs the speech runtime and model;
phones/tablets do not install them or connect directly to port 8081. Recordings
use the signed-in session and CSRF protection on every device.

Serve StockFlow through an HTTPS address with a certificate trusted by each device.
For shop Wi-Fi, the server needs a reachable host/IP and an HTTPS certificate
covering that host/IP. The localhost development certificate does not cover a
shop LAN address; ignoring certificate warnings is not a deployment solution.
Prefer an HTTPS reverse proxy forwarding to StockFlow, while keeping the speech
runtime on loopback. Keep all API traffic on the same origin; no public speech
port or separate browser API key is required. Plain HTTP on a LAN does not allow
microphone recording. The existing localhost HTTP profile is desktop development
only. Current browser versions and microphone permission are required.

Reply playback uses installed local device voices. Voice loading can be delayed;
StockFlow waits for the voiceschanged event. Some phones require a direct tap on
Read reply to start playback. Switching away from the app finishes recording
and stops playback. Native read-aloud voices and language availability can differ
between operating systems.

AI is optional. When disabled or unavailable, StockBot searches approved local
instructions across the documented application features. Outside questions show
**Contact Super Admin**. This local matcher uses known topic words; for broader
paraphrases and languages, configure and evaluate the optional provider below.
Live business enquiries are handled separately by permission-checked repositories
and dashboard services, never by model-generated SQL or model-authored figures.

The Help panel includes **Ask a question**. It uses a configured Ollama server to
select up to three relevant sections from the approved staff guides. StockFlow
returns those exact instructions, their source articles and permitted screen
links. For factual sales questions it also interprets the metric and period into
validated filters; StockFlow queries the permission-scoped records and returns
only the count, quantity or amount. The model cannot choose SQL, tenant or location,
and receives no records or figures. It does not display model-written instructions,
accept model URLs or perform transactions. Selected-language help packs translate
the approved passages while retaining English captions.

## Configure the provider

For this Windows development machine, the official standalone Ollama runtime is
stored under `%LOCALAPPDATA%\StockFlowAI\runtime`, with models under
`%LOCALAPPDATA%\StockFlowAI\models`. The VS Code build/F5 task starts it through
`.vscode/Start-StockFlowAi.ps1`, binds it to `127.0.0.1:11434`, and sets
`OLLAMA_NO_CLOUD=1`. Model downloads require internet; local inference has no paid
API or API key. CPU inference speed depends on hardware and other running programs.
These local installation files are separate from the repository and do not ship
to another machine with the build. That machine needs its own model installation.

This development machine now has `qwen3:4b` installed, and `appsettings.json`
enables it at `http://127.0.0.1:11434/` with a 90-second timeout. Thinking is
disabled for structured interpretation. Twenty-one synthetic interpretation cases
passed with the installed model and server filter guards; these include examples
used to refine its prompt and are not an independent staff accuracy benchmark.
Warm interpretation took roughly 4–6 seconds in this run, while the first cold
request took about 33 seconds. Built-in common/date questions bypass the model.
The model chooses an approved read-only tool and filters; it cannot execute URLs
or arbitrary API calls supplied by a user. StockFlow executes the matching
permission-checked repository/service and formats the recorded value directly.

Install Ollama and a model that can understand the shop's languages on the app
server or a trusted internal server. Choose the model against the evaluation
questions below and the available hardware. StockFlow does not install software
or download a model automatically.

In StockFlow.Web `appsettings.json`, configure:

```json
"HelpAi": {
  "Enabled": true,
  "Endpoint": "http://localhost:11434/",
  "Model": "name-of-your-installed-local-model",
  "TimeoutSeconds": 8,
  "PreferLocalAnswers": true
}
```

On other machines, keep **Enabled** false until a model is installed and reachable. Restart with F5
after configuring it. Normal help search and common business questions work without
AI. Model outages offer Retry for questions that need interpretation. No database
script is required.

Common how-to questions use approved, permission-filtered help directly when
`PreferLocalAnswers` is true. Supported business questions still query live shop
records; their totals are never cached as help answers. Other wording may require
the local model. Its eight-second default deadline includes waiting for a model
slot, and a busy model is rejected after 250 ms instead of queuing users for seconds.
Slow models return the existing unavailable/approved-help fallback. Set
`PreferLocalAnswers` to false only when evaluating model passage selection.
Microphone transcription is a separate local speech step before the same question
endpoint; its speed depends on recording length and server hardware. Restart the
web application after changing these settings; no database script or APK rebuild
is needed.

The endpoint is server configuration; users cannot supply it in questions. Use a
local model when questions must stay on your infrastructure. Ollama may also use
cloud models, so verify the selected model and deployment rather than assuming
all Ollama calls are local. Do not expose the model server publicly. The integration
uses the official [chat API and structured output format](https://github.com/ollama/ollama/blob/main/docs/capabilities/structured-outputs.mdx).

## Permissions and fallback

Only signed-in accounts can ask questions. Requests require the existing CSRF
token, accept 3–500 characters and allow one in-flight request per account per
application instance, followed by a three-second cooldown. Multi-instance
deployments should also apply limits at their gateway.

The server filters guide sections before sending them to the model. Sensitive
return and warranty actions require their corresponding permissions. A rejected
source ID, duplicate source, unsupported answer, missing provider or timeout
produces a fallback instead of model text. **Contact administrator** shows what
to share; it does not send a message or create a support ticket.

Question and answer text is not persisted by StockFlow monitoring or error logs.
Validated date/metric/search filters remain briefly in conversation memory. Staff
should avoid including passwords or customer details. Review model-server logging
independently.

## Evaluate before enabling for staff

The [60 representative questions](../../tests/StockFlow.MasterDataChecks/help-ai-questions.json)
cover the six guides, unsupported topics, requests for live data, attempted
instruction overrides and Hindi/Telugu questions. They are prepared examples,
not a claim that real staff have submitted or reviewed them.

`--help-ai-checks` runs the API and grounding checks with a controlled provider.
It checks approved excerpts, source IDs, unsupported fallback, permissions,
authentication, CSRF, input limits and cooldown. This verifies application
behavior, not AI understanding or accuracy.

For an installed local model, set `STOCKFLOW_HELP_MODEL` and run the existing
MasterDataChecks project with `--help-ai-live`. This uses mocked StockFlow account
permissions and the real model at localhost:11434, without database changes.
It writes `tests/bin/help-ai-live-report.json` and fails if expected sources or
fallbacks do not match. It does not verify that every selected section is the
best answer; review the returned sections with the question.

Before rollout, replace or supplement the examples with 50–100 anonymized real
staff questions. Check whether each selected section answers the question,
unsupported requests stay unsupported, restricted tasks are omitted, screen
links work and translated instructions are understood. Include users with
different roles and questions in every language the shop intends to use.
Native-speaker review of the help packs remains separate from source matching.

No live-model or native-language accuracy result should be claimed until these
checks are actually performed with the selected model and staff.

`--help-intent-live` evaluates 21 synthetic business questions against the installed
local model, including payments two days ago, current stock, counts, Hindi/Telugu,
unsupported profit/employee filters and instruction overrides. It writes
`tests/bin/help-intent-live-report.json`. This checks interpretation only; the API
checks verify permitted tool routing with fake records. Neither proves live
database totals or universal understanding of every possible question.

## Voice questions and replies

Open StockBot Assist and select the microphone. Say a question, review and correct
the recognized text, then select Send. It uses the same authenticated endpoint as typing.
Speech is not submitted automatically. Tap the microphone again or **Stop recording**
to finish, or **Cancel recording** to discard. Recording stops after sixty seconds.
Text remains editable when transcription fails, and typed questions continue to work.

The local multilingual model detects the spoken language. See **Microphone questions**
above for runtime installation and microphone requirements. When browser recording
is unavailable, the older browser recognition fallback uses the header language,
prefers an installed language pack and asks before using its online speech service.
That fallback stops after twenty seconds. No paid speech API or API key is needed.

Select **Speak replies** for automatic read-aloud, or **Read reply** for the
current answer. Playback uses installed local voices for the selected language,
falling back to an installed English voice with a notice. If no local voice is
available, the answer remains readable. Closing the panel, changing language or
starting a new question stops voice activity.

`tests/StockFlow.UiChecks/help-voice-offline.cjs` checks transcription submission,
online-service opt-in, microphone denial, cancellation, language selection,
local read-aloud and responsive controls using mocked browser speech APIs.
`tests/StockFlow.UiChecks/help-recording-offline.cjs` exercises real browser audio
recording and WAV conversion with a test microphone, plus Stop, Cancel, permission
denial, exact recording retry and discarded late results. `--help-voice-checks`
checks API authorization, CSRF and audio validation. `--help-voice-live` verifies the
installed speech runtime against the official whisper.cpp English sample.
Actual microphone transcription and device voices require testing on the shop's
browsers and devices. No database migration is required.

## Business questions and follow ups

Data is fetched after a question and stays inside the assistant. Counts and amounts
are concise. How-to questions continue to show approved instructions.

| Question | Calculation |
| --- | --- |
| Quantity sold for brand Samsung yesterday | Completed sale item quantities for the exact brand |
| Sales for product Cable today | Recorded item line values, without allocating invoice payments or header discounts |
| Customer dues for Ranga | Completed invoices less returns and non-credit payments, plus refunds |
| Supplier balance for Supplier One | Completed purchases less supplier payments and purchase returns |
| How many items returned yesterday | Customer return item quantities by return date |
| How much money refunded today | Recorded customer refund amounts by return date |
| Payment breakdown today | Sales collections grouped by mode, excluding CREDIT; refunds and service collections are separate |
| How much UPI collected last week | Sales collections in UPI by payment date |

Exact product names, brand names, variant item codes and barcodes are supported.
Unknown or ambiguous product/account matches prompt for clarification, rather than
returning zero. Named customer accounts accept an exact name or mobile number;
suppliers accept an exact name or supplier code. Balances include only records
visible through the signed-in user's business/location scope.

“Quantity sold yesterday” → “And last week?” → “Only Samsung?” retains the metric
and dates. Another date retains that product filter. A missing date is clarified
inside chat. Context lasts ten minutes of inactivity, remains separate per page
and business/location, and is cleared by unrelated guide questions. It is held in
server memory; restarting or changing application instances loses this context.
Permission checks run again for every follow-up.

Sales and return enquiries use `ShopTime:TimeZoneId`, default `Asia/Kolkata`.
Legacy sales/payment timestamps are interpreted in the database session timezone,
as in cash closing. Keep that database timezone consistent with the timezone used
when legacy records were written. Date boundaries are consecutive shop-local
midnights, including daylight-saving changes. One configured shop timezone applies
to this deployment; separate per-business timezone settings are not implemented.

A confirmed zero displays normally. Failed record queries show an unavailable
message and Retry, never a fabricated zero. Retry becomes available after the
three-second request cooldown. Unsupported filter combinations ask for clarification.

## Feedback and monitoring

API replies have Helpful and Not helpful buttons. Feedback requires
authentication and CSRF, belongs to the issuing account/location and is accepted
once within fifteen minutes. Restarting the application expires outstanding votes.

`HelpLog/help-yyyy-MM-dd.jsonl`, under the deployed application directory, records
answer identifiers, statuses, approved metric names, response milliseconds and
feedback. Question text, search terms, figures, account names and audio are omitted.
Files older than thirty days are removed when events are written. Configure a
service-account-writable `HelpMonitoring:Directory`, or set `HelpMonitoring:WriteFile`
to false and collect the application logger's StockBot events instead. Use timing,
failure categories and feedback to choose topics for staff review; these logs do
not retain the wording of unanswered questions.

## Deployment and simultaneous requests

The web application's background worker can start an installed Ollama runtime
without VS Code. `HelpAi:StartLocalRuntime` defaults to true when help AI is enabled.
It starts only the loopback endpoint on port 11434, sets `OLLAMA_NO_CLOUD=1` and
reuses an already running server. An owned child process stops with the application.
It never downloads software or models.

On the deployment server, install the trusted runtime and model first. Set
`HelpAi:RuntimePath` and `HelpAi:ModelsPath` to absolute paths accessible to the web
service account, and keep `HelpAi:Model` matched to an installed model. Default paths
use that account's LocalApplicationData/StockFlowAI directory, which may differ from
the developer's account. For a separately managed model service, set
`HelpAi:StartLocalRuntime` to false. Do not run multiple independently managed
application instances that each try to own the same local model process.

One model request runs at a time per application instance; another waits up to five
seconds before a busy fallback. Common deterministic enquiries bypass the model.
Each account still has one question in flight and a three-second cooldown across
its tabs. For multiple application instances, add gateway limits, sticky routing or
shared conversation storage, and a separately managed model service.

`--help-business-checks` covers queries, follow-ups, clarification, permissions,
zero versus timeout, feedback, midnight/DST boundaries and concurrent staff calls
with controlled records. `--help-sales-database-readonly` checks PostgreSQL query
mapping under an empty business scope without writes. `--help-intent-live` exercises
synthetic questions against the installed model. Reconcile real shop figures and
measure response times with expected staff concurrency before rollout.

`tests/StockFlow.UiChecks/help-business-offline.cjs` checks no data fetch on opening,
Retry, confirmed zero, feedback and responsive layout. Voice checks verify that
final speech waits for review and edits before Send.

## Operational enquiries

StockBot fetches these records only after a question is sent. Answers stay inside
the assistant and use the signed-in business, permitted stores and locations.
These tools are read-only; the model cannot create orders or change records.

| Question | Answer basis |
| --- | --- |
| Which purchase orders are still waiting for stock? | Draft and partially received orders; ordered minus received and damaged units. Draft status is shown explicitly. |
| Which products need purchasing, based on minimum stock? | Active variants at or below their location minimum. Suggested quantity brings stock one unit above minimum; review pending purchases before ordering. This is not demand forecasting. |
| Which products sold most this month? | Products ranked by completed sale units in the stated shop-date period, before returns. |
| Slow-moving products last month | Fewest completed sale units among active variants currently in stock and created before the period starts. Includes zero-sale variants; this does not establish how long stock was available. |
| Which customers have overdue dues? | Positive invoice balances whose recorded payment due date is before today. Payments, returns and refunds are reconciled using the existing balance rules. Unknown dates are reported separately. |
| Which repairs are overdue or ready for collection? | Undelivered, uncancelled repairs with an expected delivery date before today or status READY. Each condition can also be requested separately. |
| When does warranty expire for serial number SN123? | The latest warranty for that exact serial; expiry and recorded status. Missing serials prompt clarification. |
| Which transfers are pending receipt? | Dispatched or partially received transfers; dispatched minus received, damaged and lost units. Source/destination visibility follows existing transfer rules. |
| Compare this week's sales with last week | Completed invoice revenue: this week to date versus the full previous week. Dates, difference and percentage are shown; no percentage is calculated against a zero baseline. This month versus last month also works. |

Lists show up to ten records and indicate when more matching records exist.
Rankings require an explicit date or period. Historical reorder/repair status,
filtered rankings and arbitrary comparison pairs are not supported and must not
be substituted with unfiltered figures. Operational replies currently retain
English text; existing caption translation continues where translations exist.

### Customer payment dates: manual migration 037

After migration 036, manually run
[037_customer_payment_due_dates.sql](../../database/migrations/037_customer_payment_due_dates.sql)
as the database administrator, then restart StockFlow. The script adds an optional
date and a scoped index; it does not assign dates to existing invoices. It has not
been executed by the coding agent.

Historical note: migration 037 introduced optional dates for older credit flows.
Current New Sale requires full payment by one method and accepts no new credit,
split checkout or payment due date. Older credit invoices, their recorded due
dates and authorized collections remain supported. Missing schema/date evidence
must be explained rather than replaced with invented dates or zero balances.
Follow the actual deployment's migration baseline; this is not an instruction to
rerun old migrations.
Existing invoices without dates remain unclassified; agreed dates must be
recorded separately by an authorized administrator before overdue reconciliation.

### Assistant timeout or canceled operation

An HTTP call to the model can raise `TaskCanceledException` with an aborted socket
when the configured `HelpAi:TimeoutSeconds` expires, or when the user closes a
request. Model timeouts are classified as provider failures: approved local help
remains available, and interpreted business questions report unavailable rather
than inventing a total. Caller cancellation still stops the request normally.

Restart StockFlow after updating this code. If the debugger pauses on a thrown
cancellation, Continue lets the cancellation handler run; a debugger exception
pause alone does not establish an application crash. Check that Ollama is running,
the configured model is installed, and that model responses complete within the
configured timeout. A successful `/api/tags` health request confirms service
availability, not model response speed. No database migration or APK rebuild is
required for this server-side handling change.
