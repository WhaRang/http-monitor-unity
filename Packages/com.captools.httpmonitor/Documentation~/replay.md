# Replay and the composer

Send a captured request again from the Editor, as it was or after editing it.

## Replay

Select a request and click **↻ Replay** in the detail strip, or right-click a row ▸ **Replay**.

The request is sent again and appears as a new row with a teal **R** badge. The new row is
selected as soon as it exists, shows as pending, and fills in when the response arrives. Its
detail strip carries a **replay of #12** link back to the original.

![A replayed request, with the R badge and the link to its original](images/replay-row.png)

Two clicks on Replay answer "is this endpoint flaky". Replay then **Pin** the original answers
"did the response change".

What happens on the click depends on the request:

| The request | What happens |
|---|---|
| GET, HEAD, OPTIONS with no redacted headers | Sent immediately. |
| Any other method | You are asked first, because resending may repeat its effect on the server. The dialog offers **Send and don't ask again**. |
| Has a redacted header whose value you have not supplied | The composer opens so you can paste it. |

## Edit and resend

**Edit & resend…** in the detail strip, or on the row menu, opens the composer filled in from the
request. Change anything, then **Send** or **Ctrl+Enter**.

![The composer, filled in from a captured POST](images/composer.png)

- **Method**: the common ones in a dropdown, or **Custom…** for any other token.
- **URL**: absolute, `http` or `https`.
- **Follow redirects**: off records the 3xx itself rather than following it.
- **Timeout**: the replay is recorded as aborted when it elapses. It covers the whole exchange,
  including a body that stalls midway.
- **Headers**: add, edit and remove rows. `Host` and `Content-Length` are computed by the client
  and ignored if present.
- **Body**: free text. **Format JSON** pretty-prints it, and says so when it is not valid JSON.
  A binary body is kept as captured and sent as-is; **Clear** removes it so you can type one.

The line at the bottom reports the result (status, time, size) with **Show in list**.

The composer is also available empty, for a request from scratch:
**Window ▸ Analysis ▸ HTTP Request Composer**.

## Redacted headers

The SDK never stored your token, so it cannot resend it. A redacted header arrives in the composer
as an empty password field marked **redacted**.

![A redacted Authorization header in the composer](images/composer-redacted.png)

Paste the real value once. It is remembered **for this Editor session only**, in memory, scoped
to the host and header name. After that, one-click Replay works for requests to the same host
without opening the composer. The value is never written to disk, to a record, or to a HAR
export, and a script recompile or Editor restart forgets it.

## Three limits

- **The Editor sends the replay, not your game.** The User-Agent, cookie jar and certificate
  handling are the Editor's. Replay is good for "did the server change" and "what does this
  field do". It does not reproduce something that only happens on a device.
- **A replay repeats the request's effect.** Replaying a POST that submits a score submits it
  again. That is why non-GET methods ask first.
- **Only `http` and `https` can be replayed.** A record made through the manual API for a custom
  scheme is shown, but the composer will tell you it cannot send it.

Replays are recorded even while **Record** is off. You asked to send it and see it.
