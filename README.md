# Release Manager

A web application for managing program releases, packaging source files into ZIP archives, uploading release packages to FTP/FTPS, and preparing release communications.

## Features

- **Dashboard and release tracking**
  - View programs and releases.
  - Track release status, version, dates, PIC, and release notes.
  - Filter and search releases.

- **Program management**
  - Store a program’s name, current version, and source folder.
  - Choose which file extensions are included in ZIP packages.
  - Configure FTP destination folders and email recipients for programs.

- **Release management**
  - Add release items with individual versions, categories, references, and descriptions.
  - Record release item dates and the user who added each item.
  - Set a release status manually to **READY QA**; other statuses are calculated from the version and release checklist.

- **ZIP packaging**
  - Generate ZIP files in the program’s source folder.
  - Preserve the included files’ names and relative paths.
  - When extensions are configured, include files named after the program, such as `posBpb.dll` and `posBpb.pdb`.
  - Check DLL and EXE file versions against the release version before packaging.
  - Save the generated ZIP filename and local path with the release.

- **FTP/FTPS uploads**
  - Upload ZIP packages using the configured FTP connection.
  - Save the resulting FTP location with the release.

- **Release checklist and status**
  - Track ZIP generation, FTP upload, email preparation, and WhatsApp sending.
  - Calculate release status from the final version number and checklist completion.
  - Show release progress while the combined **Release** action runs.

- **Release communications**
  - Prepare a prefilled Gmail compose message.
  - Open a WhatsApp message with the release text.
  - Record completion after the communication is sent.

- **Accounts and permissions**
  - Support user accounts and roles.
  - Restrict account and settings management to administrators.
  - Limit non-admin release controls to the main **Release** action.

## Technology stack

- **Backend:** ASP.NET Core MVC, C#
- **Database access:** Entity Framework Core
- **Database:** MySQL
- **Frontend:** Razor views, HTML, CSS, Bootstrap, and JavaScript
- **FTP/FTPS:** FluentFTP
- **Email:** Prefilled Gmail compose URL; SMTP settings are also present in the application

## Requirements

- .NET SDK compatible with the project’s target framework
- MySQL server
- Access to the program source folders
- FTP/FTPS server access for uploads

## Configuration

Configure the database connection and application settings in `appsettings.json`. Add FTP settings through the application’s Settings area. Program records need a valid source folder and may specify the extensions to include in release ZIP files.

## Running locally

```bash
dotnet restore
dotnet run