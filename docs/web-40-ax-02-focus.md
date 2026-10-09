# WEB.40 AX-02 focus record: Account and Chat

Status: recorded under WEB.40 for the review fix of 2026-10-09 (focus placement, retirement item 8 of [web-40-react-retirement.md](web-40-react-retirement.md)).

Requirement: Design `docs/requirements/12-quality-and-compatibility-contract.md`, AX-02 (line 184): "focus is always visible; focus order is logical; ... after an operation, focus returns to a sensible origin". P2-021 item 8 makes this a precondition for retiring each old UI.

## Why this record exists

A focused control that becomes `disabled` or is removed from the document drops keyboard focus to the body. bUnit does not move browser focus, and the opt-in axe check does not detect focus loss, so neither proves AX-02. The page therefore never disables or removes a control that has keyboard focus. A control that cannot act is marked `aria-disabled="true"` and refuses the action in its handler. Moving focus to another element would need a new JavaScript interop call, and the brief limits interop to the audited points (P2-021 item 2), so no focus call is used.

## Control states

| Screen | State | Control | Element and focus |
| --- | --- | --- | --- |
| Chat | idle | Send (`type=submit`) | Enabled. Enter in the Name field submits through it (the form's default button). |
| Chat | pending | Send | Same element, `aria-disabled="true"`, label "Sending…". A click or Enter is refused by the send guard, so no second message is sent. Focus stays on Send. |
| Chat | idle | Cancel (`type=button`) | Always rendered, so it is never removed: `aria-disabled="true"` and `tabindex="-1"`. It is not a tab stop, and a click is refused by the handler (option a of the 2026-10-09 adjudication). |
| Chat | pending | Cancel | Same element, `aria-disabled="false"` and no tabindex, so it joins the tab order after Send. A click cancels the pending message. |
| Chat | after the message ends | Cancel | Same element, back to idle (`aria-disabled="true"`, `tabindex="-1"`). Focus that was on Cancel stays on it, because an aria-disabled control is never removed. |
| Chat | after reply or notice | Send | Same element, `aria-disabled="false"`. Focus stays on Send. |
| Account | anonymous first read | none | No control, so the tab sequence is empty. |
| Account | signed in | Sign out | Enabled. |
| Account | sign-out in flight | Sign out | Same element, `aria-disabled="true"`, label "Signing out…". A click is refused, so no second sign-out is posted. Focus stays on it. |
| Account | session ended by sign-out | Try again | The same element, relabelled and enabled. Focus stays on it. |
| Account | sign-out refused or unconfirmed | Try again | The same element, relabelled and enabled. Focus stays on it. |
| Account | read failed | Try again | Enabled. A click makes the same element `aria-disabled="true"` while the read runs. |
| Account | read answers signed in | Sign out | The same element, relabelled and enabled. |

The Account action is one element for the whole visit, created when the page first shows a session or a failure. Its label and `aria-disabled` state change; the element is never removed.

## Checks

bUnit (the CI accessibility gate, brief section 6 decision 3). bUnit re-parses the markup on every render, so these prove that the control is present and reachable at each step, the tab sequence and the refused actions. They do not prove element identity:

- `tests/ArcForges.Web.App.Tests/ChatPageTests.cs`: `SendStaysPresentAndFocusableThroughAPendingMessage`, `APendingSendIgnoresAClickAndEnterSoItCannotSendTwice`, `ChatTabsFromTheNameFieldToSendAndReachesCancelOnlyWhileAMessageIsPending`.
- `tests/ArcForges.Web.App.Tests/AccountPageTests.cs`: `SignOutStaysPresentAndFocusableWhileWorkingAndAfterTheSessionEnds`, `AFailedSignOutKeepsTheControlAndOffersTryAgainInIt`, `TryAgainStaysPresentAndDisabledWhileItsReadRunsAndBecomesSignOut`.

Browser (local opt-in only; never CI, see `tests/browser/ArcForges.Web.Browser.Tests/LocalFocusBrowserTests.cs`). It drives Chromium with the keyboard and asserts `document.activeElement` is the same element before, during and after each operation. The same-origin answers are fixed by Playwright routes:

- `ChatKeepsFocusOnSendWhileAMessageIsPendingAndAfterTheReply`
- `AccountKeepsFocusOnSignOutWhileSigningOutAndAfterTheSessionEnds`

Run: set `ARCFORGES_LOCAL_BROWSER=1` and `ARCFORGES_BROWSER_BASE_URL` to a served build of the Account and Chat profiles, then run `dotnet test tests/browser/ArcForges.Web.Browser.Tests`. Result on the recording machine: see the record at the end of this file.

## Remaining (recorded, not fixed here)

1. **Cancel in Chat (resolved, option a).** Cancel is always rendered. While idle it is `aria-disabled="true"` and `tabindex="-1"`, so it is never a tab stop and never removed, and focus cannot fall to the body when a message ends. The bUnit tests `CancelStaysInTheDocumentWhileIdleAndAClickOnItIsRefused` and `CancelBecomesTabbableWhileAMessageIsPendingAndReturnsToIdleWithoutBeingRemoved` assert the states; the browser focus identity is asserted by `LocalFocusBrowserTests` for Send and Sign out, and the Cancel states are part of the axe re-proof in [web-40-accessibility.md](web-40-accessibility.md). Decision: 2026-10-09 coordinator adjudication (brief section 10); option (b), a combined Send/Cancel control, was not chosen.
2. **WCAG 2.2 AA verification per replacement screen.** P2-021 item 8 also requires a recorded WCAG 2.2 AA verification for Account, Chat and the Site pages. That record is [web-40-accessibility.md](web-40-accessibility.md). This record covers focus only (AX-02).
3. **Browser run on the recording machine.** The browser check runs only with the local opt-in. It is not part of CI (P2-017), and hosted CI does not run it.

## Record of the local browser run

Recorded on the WEB.40 worker machine, 2026-10-09, against the Release publish of the Account and Chat profiles on this branch (`dotnet publish src/ArcForges.Web.App -c Release`), served on a local origin with single-page fallback:

- `LocalFocusBrowserTests`: 2 passed (`ChatKeepsFocusOnSendWhileAMessageIsPendingAndAfterTheReply`, `AccountKeepsFocusOnSignOutWhileSigningOutAndAfterTheSessionEnds`). The Chromium build was the installed one, named by `ARCFORGES_CHROMIUM_PATH`.
- The other browser tests in the project (`LocalBrowserTests`, `LocalSiteBrowserTests`, `LocalBuildIdentityBrowserTests`) did not launch on this machine: the Playwright package expects Chromium build 1234, which is not installed (Chromium 1243 is). That is an environment gap, not a result of this change, and it is recorded here rather than worked around.
- A negative control (the pre-fix `disabled` markup in the browser) was not run. The check fails on any lost focus by identity, so it cannot pass with a disabled control, but the control itself was not executed.
