CREATE DATABASE IF NOT EXISTS release_manager CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE USER IF NOT EXISTS 'release_app'@'%' IDENTIFIED BY 'CHANGE_ME';
GRANT ALL PRIVILEGES ON release_manager.* TO 'release_app'@'%';
FLUSH PRIVILEGES;
-- The application creates its tables automatically on first start.
-- MySQL 8.0+ is required.

-- Existing installations missing the release-level status can apply this
-- manually if the application has not yet been restarted with the schema fix:
-- ALTER TABLE Releases ADD COLUMN Status varchar(30) NOT NULL DEFAULT 'ON DEVELOPING';
-- ALTER TABLE Releases ADD COLUMN Notes varchar(4000) NULL;
-- Existing installations can add the release-item add-time and creator fields:
-- ALTER TABLE ReleaseItems ADD COLUMN CreatedAt datetime(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6), ADD COLUMN UserId int NULL;
-- ALTER TABLE ReleaseItems ADD COLUMN ProgramVersion varchar(50) NOT NULL DEFAULT '';
-- UPDATE ReleaseItems AS ri JOIN Releases AS r ON r.Id = ri.ReleaseId SET ri.ProgramVersion = r.Version WHERE ri.ProgramVersion = '';
-- For installations that used the earlier manual Date field, copy it once:
-- UPDATE ReleaseItems SET CreatedAt = Date;
-- Existing installs retain the removed checkbox column; give it a default so new item rows save:
-- ALTER TABLE ReleaseItems ALTER COLUMN Complaint SET DEFAULT 0;
-- Program ZIP file-type filter (blank includes all file types):
-- ALTER TABLE Programs ADD COLUMN ZipFileExtensions varchar(500) NULL;
-- Release approval checklist timestamps:
-- ALTER TABLE Releases ADD COLUMN EmailPreparedAt datetime(6) NULL, ADD COLUMN WhatsAppSentAt datetime(6) NULL;
-- Store the ZIP's source path with each release so later program path changes do not change its location:
-- ALTER TABLE Releases ADD COLUMN ZipFilePath varchar(1000) NULL;
