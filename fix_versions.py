import os

files = [
    'README.md',
    'RELEASE_NOTES.md',
    'installer/RemoteLAN_Setup.iss',
    'src/RemoteLAN.Protocol/Discovery/DiscoveryConstants.cs',
    'src/RemoteLAN.Protocol/RemoteLAN.Protocol.csproj',
    'src/RemoteLAN/MainWindow.xaml',
    'src/RemoteLAN/RemoteLAN.csproj',
    'src/RemoteLAN/Views/SessionWindow.xaml',
    'src/RemoteLAN/Views/SettingsWindow.xaml',
    'tests/RemoteLAN.Tests/RemoteLAN.Tests.csproj'
]

for f in files:
    with open(f, 'r', encoding='utf-8') as file:
        content = file.read()
    content = content.replace('1.1.0', '1.1.3')
    with open(f, 'w', encoding='utf-8') as file:
        file.write(content)

with open('RELEASE_NOTES.md', 'r', encoding='utf-8') as file:
    rn = file.read()

new_notes = '''
## [1.1.3] - 2026-09-17

### Fixed
- Restored original UI iconography that was inadvertently corrupted by build tooling.

## [1.1.2] - 2026-09-17

### Fixed
- Fixed an issue where the OS password injection and general remote lock screen input were not functioning due to User Interface Privilege Isolation (UIPI) and Secure Desktop restrictions. RemoteLAN now seamlessly elevates to a SYSTEM process within the interactive session on launch, fully unlocking lock screen integration and interactions.
- Improved the grace period for discovered devices in the network list before they are marked as offline, minimizing UI flicker when UDP discovery packets are delayed or lost.
'''

rn = rn.replace('# Release Notes', '# Release Notes\n' + new_notes)

with open('RELEASE_NOTES.md', 'w', encoding='utf-8') as file:
    file.write(rn)

print('Done')
