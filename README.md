# EmbyTogether

EmbyTogether enables multiple clients to watch content simultaneously. There is no User-Interface, so sessions have to be automagically generated.
Because of this, it is only possible to synchronize clients logged into the same user account.

When one client Pauses, Unpauses or Seeks, the command is propagated to all other clients on the same account that are playing the same content.

The commands are extracted from status messages, which each client periodically (once per second) sends to the server. Because of latency and the 'sparseness' of these messages the sync will never be perfect.
Better sync would require client modifications and is not possible via a simple plugin.

Currently only tested with local users.

### Compile & Install
Simply clone the repo and open `embytogether.sln` in VisualStudio 2017 (Community Edition). Compile the project and copy `embytogether\bin\Debug\netstandard1.3\embytogether.dll` to your plugin directory (located in `EMBY\programdata\plugins`).

Then restart Emby, go to the Plugin configuration section and select on which user accounts syncing should be active.

### Known Issues
- Seeking leads to problems, since the seek is only recognized after the inizializing cilent has buffered and starts playing. 
  The other then receive the seek command and have to catch up, get delayed. This can be worked around by seeking again, into an area already buffered (10sec in the past?)
- No User Interface to join a session -> Need to constrict a session to clients logged into the same user.