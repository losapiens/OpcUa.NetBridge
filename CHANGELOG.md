# Changelog

All notable changes to **OpcUa.NetBridge** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com),
and this project adheres to [Semantic Versioning](https://semver.org).

---

## [Unreleased]

---

## [1.0.0-beta.4] - 2026-11-27

### Added

- `ReadAsync(IEnumerable<Tag>)` overload to read previously discovered tags directly while preserving their `DisplayName` (`Tag.Name`).

### Fixed

- Improved `Tag.Name` fallback extraction in `NodeAddressHelper` to strip surrounding quotes from segmented NodeIds (such as Siemens S7-1200 / S7-1500 addresses like `ns=3;s="DB"."Tag"`).

---

## [1.0.0-beta.3] - 2026-11-27

### Fixed

- Set explicit `Directory` store type on `ApplicationCertificate` and fixed `ApplicationUri` formatting for encrypted connections (`security: true`).
- Improved PLC certificate validation compatibility (`BadCertificateUseNotAllowed`, `BadCertificateTimeInvalid`, `BadCertificateUntrusted`) when `untrusted: true` is enabled.

---

## [1.0.0-beta.2] - 2026-11-27

### Added

- Fully asynchronous API (`*Async`) with batch (`IEnumerable`) support across `ReadAsync`, `WriteAsync`, `MonitoringAsync`, `DevicesAsync`, `GroupsAsync`, and `TagsAsync`.
- Decoupled `TagEventArgs` subscription notifications carrying `Tag` objects instead of raw `OPCFoundation` SDK `MonitoredItemNotification` types.
- UTC `Timestamp` property (`SourceTimestamp` with fallback to `ServerTimestamp`) and automatic `DisplayName` resolution on `Tag`, `Group`, and `Device`.
- Support for mixed address formats (`string`, numeric, and raw `ns=...` NodeIds) in single and batch operations.
- Self-hosted in-memory OPC UA server integration test suite.

### Changed

- Marked all legacy synchronous methods as `[Obsolete]` in favor of `*Async` equivalents.

---

## [1.0.0-beta.1] - 2026-11-26

### Added

- Initial release of `OpcUa.NetBridge` targeting `.NET 8.0`.
- Upgraded underlying `OPCFoundation.NetStandard.Opc.Ua` dependencies to `v1.5.378+`.
- Automatic, permission-safe certificate store resolution for desktop applications, Windows services (with restricted Windows ACLs), and debug sessions.
- Isolated PKI folders per `ApplicationName`.
