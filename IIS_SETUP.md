# IIS Configuration for Persistent Blazor Server

The included `web.config` disables ASP.NET Core request timeouts so the site won't disconnect during long operations.

1. **Deploy `web.config`**
   - Copy `ExportCheckoutBlazor/web.config` to the website root on the IIS server.
   - Ensure the path in `stdoutLogFile` exists or adjust it.

2. **Application Pool Settings**
   - In IIS Manager, select the application pool for this site.
   - Set **Idle Time-out (minutes)** to `0` to prevent the app pool from stopping after periods of inactivity.
   - Optionally disable **Ping Enabled** or raise **Ping Maximum Response Time** to avoid worker process recycling.

3. **Additional Considerations**
   - If using a proxy or load balancer in front of IIS, verify its request timeout settings are also increased or disabled.
   - The server's firewall or network appliances should allow long-lived connections if SignalR is used.
