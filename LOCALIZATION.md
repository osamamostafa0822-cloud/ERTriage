# English and Arabic resources

The backend supports English (default) and Arabic. Send `Accept-Language: ar`
or `Accept-Language: ar-EG` for Arabic, and `Accept-Language: en` for English.
Unsupported or missing language headers fall back to English. Weighted language
preferences are supported. Responses include the selected `Content-Language`.

Resources are in `src/ERTriage.Api/Localization/Messages.resx` (English) and
`Messages.ar.resx` (Arabic). Both files use the same keys and format placeholders.
The API registers `AddLocalization()` and injects `IStringLocalizer<Messages>`.
Use `localizer["Key"]` or `localizer["Key", arguments]` for API display text.
Resource lookup uses the request's UI culture; parameter formatting uses its
current culture. Stored triage values and diagnostic fallbacks remain invariant.

Domain and Application carry `MessageDescriptor` values containing a stable key,
arguments, and an English diagnostic template. `MessageCatalog` creates these
descriptors without looking up resources or accessing request culture. Exception
messages and validator results therefore remain deterministic. `MessageLocalizer`
translates descriptors (including nested field labels) only in the API. Missing
keys fall back to the diagnostic message. Metadata is excluded from the JSON error
contract. For a new business error, add its diagnostic template to the catalog
and the matching English/Arabic resource entries.

Coverage includes application and domain validation, pagination, not-found and
conflict errors, unexpected API errors, model-binding failures, field labels,
units, and triage explanations returned in patient and board responses.
Status and waiting-level labels are available as resource entries; enum values,
JSON field names, routes, and database values remain stable.

Triage calculation and persisted explanations remain invariant English.
`LocalizedResponseFilter` applies the injected `TriageReasonLocalizer` before
MVC serialization. It handles patient DTOs, board DTOs, paged board results, and
their typed `GeneralResponse<T>` envelopes, making translated copies without
recalculating priority. Application DTO mappings remain language-independent.
Unknown historical explanation text is preserved. Future response shapes must be
added to the filter explicitly.
When introducing a new triage band, add its resource pair and mapping.
Patient names, complaints, override reasons, and disposition notes are user data
and are returned unchanged.

Developer logs, startup configuration diagnostics, source comments, seed patient
data, and technical documentation remain English. No frontend exists yet in this
repository; a future frontend will need its own UI resources and Arabic RTL layout.

Verification: `dotnet test ERTriage.sln --filter "FullyQualifiedName!~Persistence"`.
Localization tests cover request-language selection, regional fallback, translated
error envelopes and validation, persisted-data preservation, and matching resource
keys and format placeholders, missing-key fallback, and translation of exceptions
created before an Arabic request.

Enum display labels: `PatientStatus` and `WaitingLevel` members carry `[MessageKey("...")]`
(Domain attribute, key only, no translation). The key matches an entry in both resx files.
`WaitingLevel.None` has no label because it means "no indicator". `MessageCatalog.Status`
reads the status keys from the attribute. Enum values in JSON, routes and the database are unchanged.
No API code displays `WaitingLevel` labels yet; the keys are ready for the future frontend or a label endpoint.
