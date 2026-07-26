After resolving any architectural issue, bug, inconsistency, race condition, or integrity risk:



You MUST update the appropriate spec-kit files.



Rules:



\* Update KNOWN\_RISKS.md for:



&#x20; \* newly discovered risks

&#x20; \* mitigated risks

&#x20; \* accepted tradeoffs

&#x20; \* architectural lessons



\* Update ARCHITECTURE\_TRUTH.md if:



&#x20; \* ownership boundaries changed

&#x20; \* synchronization model changed

&#x20; \* compensation logic changed

&#x20; \* consistency strategy changed



\* Never duplicate existing entries.



\* Consolidate related findings.



\* Keep entries concise and technical.



\* Mark whether the issue is:



&#x20; \* CONFIRMED

&#x20; \* MITIGATED

&#x20; \* ACCEPTED

&#x20; \* THEORETICAL



Always treat the spec kit as persistent engineering memory.



