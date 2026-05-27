using System.Net;
using System.Net.Sockets;

public class PortFinder
{
    public static int StartOnAnyAvailablePort()
    {
        // Bind to port 0 to let the OS assign an available port
        TcpListener listener = new TcpListener(IPAddress.Loopback, 0);

        listener.Start();

        // Retrieve the port the OS actually assigned
        int assignedPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        // Stop it if you just needed to find the port, or keep it running if this is your actual server
         listener.Stop();

        return assignedPort;
    }
}