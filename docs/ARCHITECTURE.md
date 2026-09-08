# Architecture

- `Views/`: responsive Avalonia screens for home, publishing, connecting, and settings.
- `Models/ShareProfile.cs`: validated, credential-free `.ptlink` payload and URL-safe share-code encoding.
- `Services/CloudflaredService.cs`: account-less HTTP/HTTPS Quick Tunnel process lifecycle and URL parsing.
- `Services/NamedTunnelService.cs`: browser login, named-tunnel reuse/creation, DNS routing, local ingress configuration, and process lifecycle.
- `Services/CloudflareAccessService.cs`: Access application and email allow-policy creation using a short-lived in-memory API token.
- `Services/ClientConnectionService.cs`: client-side TCP/RDP proxy and local-port validation.
- `Services/CloudflaredManager.cs`: platform asset selection, version discovery, and official binary updates.
- `Services/AppSettingsService.cs`: atomic non-secret preferences and recent-history persistence.
- `Services/LocalizationService.cs`: runtime Chinese/English UI translation.

Every child process is started with an argument list rather than a shell command, preventing user input from becoming shell syntax. Long-running tunnel processes are terminated when the user stops a connection or closes the application.
