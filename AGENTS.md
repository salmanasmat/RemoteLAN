You will be using semantic versioning.



You will bump the version if required before making an installer.



You will not make an installer unless explicitly asked for it.



You will use Light Mode Theme unless asked otherwise.



You will update .gitignore and README files after task completion.



\## Installer and Inno Setup



When creating or modifying the installer, use Inno Setup.



The installer must support clean upgrades from a previous version of the application.



Before installing a new version:



\* Forcefully close any running instance of the application.

\* Ensure all application processes have terminated before continuing.

\* Remove the previous installed version and its application files.

\* Preserve user data, configuration, settings, logs, and other user-generated data unless explicitly instructed to remove them.

\* Install the new version into the configured application directory.

\* Do not leave files from the previous version that are no longer part of the new installation.



The installer must handle the upgrade process automatically. The user should not need to manually uninstall the previous version before installing the new version.



The installed application must:



\* Start automatically with Windows.

\* Start in the background without displaying the main application window.

\* Continue running as a background application.

\* Remain hidden when started automatically.

\* Display the main UI only when an incoming remote connection request is received or when the user manually opens the application.

\* Avoid opening duplicate application instances when the application is already running.

\* If the user manually launches the application while a background instance is already running, bring the existing instance to the foreground instead of starting another instance.



The installer should create the required Windows startup configuration for the application.



The installer must not launch the application with the main UI visible immediately after installation unless explicitly requested.



Before finalizing an installer, verify:



\* The previous application instance is terminated.

\* The previous version is replaced correctly.

\* User data and configuration are preserved.

\* The new version starts successfully.

\* Windows startup launches the application in background mode.

\* The main window remains hidden during background startup.

\* An incoming remote connection request can bring the application UI into view.

\* Manually launching the application brings the existing background instance to the foreground.

\* Multiple instances cannot be created accidentally.

\* Uninstalling the application removes application files and startup configuration while preserving user data unless explicitly requested otherwise.



