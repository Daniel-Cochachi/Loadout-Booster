-- LOADOUT · Esquema inicial v1
PRAGMA journal_mode = WAL;
PRAGMA synchronous = NORMAL;
PRAGMA temp_store = MEMORY;
PRAGMA cache_size = -64000;

CREATE TABLE IF NOT EXISTS Loadouts (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    Name TEXT NOT NULL,
    ColorHex TEXT NOT NULL DEFAULT '#C50337',
    CoverPath TEXT NOT NULL DEFAULT '',
    ColorHex2 TEXT NOT NULL DEFAULT '',
    SortOrder INTEGER NOT NULL DEFAULT 0,
    CreatedAt TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS LoadoutItems (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LoadoutId INTEGER NOT NULL REFERENCES Loadouts(Id) ON DELETE CASCADE,
    Name TEXT NOT NULL,
    Kind TEXT NOT NULL DEFAULT 'Exe',
    Target TEXT NOT NULL DEFAULT '',
    Arguments TEXT NOT NULL DEFAULT '',
    OrderIndex INTEGER NOT NULL DEFAULT 0,
    DelaySeconds INTEGER NOT NULL DEFAULT 2
);
CREATE INDEX IF NOT EXISTS idx_items_loadout ON LoadoutItems (LoadoutId, OrderIndex);

CREATE TABLE IF NOT EXISTS SessionLog (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    LoadoutId INTEGER NOT NULL REFERENCES Loadouts(Id) ON DELETE CASCADE,
    StartedAt TEXT NOT NULL,
    EndedAt TEXT
);
CREATE INDEX IF NOT EXISTS idx_sessions_loadout ON SessionLog (LoadoutId);

CREATE TABLE IF NOT EXISTS AppSettings (
    Key TEXT PRIMARY KEY,
    Value TEXT NOT NULL
);
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('MaxFreeLoadouts', '3');
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('RankedDnd', 'true');
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('MinimizeToTray', 'true');
INSERT OR IGNORE INTO AppSettings (Key, Value) VALUES ('StartWithWindows', 'false');
