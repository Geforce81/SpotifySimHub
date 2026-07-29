# Manual verification

Run this checklist only after Debug and Release builds succeed. These steps require explicit approval because they start SimHub and a real Spotify login.

- [ ] SpotifySimHub appears under Additional plugins.
- [ ] No SDK demo UI is visible.
- [ ] First startup without a token does not open a browser.
- [ ] The initial status is `Login required`.
- [ ] Connect starts the Spotify login.
- [ ] The loopback callback completes successfully.
- [ ] The settings page reports `Connected`.
- [ ] Artist, track, and album are displayed.
- [ ] Cover art is displayed.
- [ ] All six documented SimHub properties work in a dashboard.
- [ ] Restarting SimHub reuses the saved refresh token.
- [ ] Restart does not require a new browser login.
- [ ] No active playback produces the expected status.
- [ ] Track changes update the metadata.
- [ ] Track changes update the cover art.
- [ ] Disconnect clears the token and playback data.
- [ ] Restart after Disconnect does not open a browser.
- [ ] Reconnect succeeds.
- [ ] SimHub closes without a hanging listener or plugin error.
- [ ] Logs contain no access token, refresh token, or client ID.
