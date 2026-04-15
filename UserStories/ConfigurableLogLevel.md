# Configurable Log Level

## Story

As a logged-in user of Alexandria,
Wants to change the application log level between Debug, Information, Warning, and Error.
So that I can turn verbosity up while troubleshooting and back down afterwards without editing configuration files or restarting the server.

## Technical

Add a new "Log level" tile to the Settings page and Settings modal, alongside the existing tiles (Sync batch size, 
Words per page, Proxy traffic logging, Sync library at boot). The tile presents a dropdown with four options: 
Debug, Information, Warning, Error. The default on a fresh install is Information.

The chosen level is persisted in the existing `Settings` table as a new `LogLevel` column and 
flows through the existing settings endpoints (`GET /api/alexandria/settings` and `PUT /api/alexandria/settings`) alongside the other 
settings fields. Existing databases pick up the new column automatically on startup with a default of Information so no data is lost.

The change takes effect immediately — the next log entry respects the new minimum level without restarting the server. 
Only the default log category is overridden; the existing category-specific overrides for `Microsoft.*` and `System.Net` 
defined in `appsettings.json` continue to apply so framework noise stays suppressed even when the user selects Debug.

On server startup, the persisted value is read from the database and applied before the application begins serving requests, 
so the selected level survives restarts.

## Acceptance Criteria

- [ ] Settings page and Settings modal each show a "Log level" tile with a dropdown containing Debug, Information, Warning, Error
- [ ] A fresh install defaults to Information
- [ ] Saving a new value persists it and takes effect immediately without a restart
- [ ] The selected level survives application restarts
- [ ] Existing databases from before this change migrate automatically to include the new column with a default of Information
- [ ] Framework category overrides (`Microsoft.*`, `System.Net`) from `appsettings.json` remain in effect regardless of the selected level
- [ ] Selecting Debug produces additional log output that is not shown at Information
- [ ] Selecting Warning or Error suppresses Information-level entries

## Dependencies

The user is authenticated and logged into Alexandria.
