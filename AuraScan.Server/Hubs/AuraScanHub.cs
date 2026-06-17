using Microsoft.AspNetCore.SignalR;

namespace AuraScan.Server.Hubs;

public class AuraScanHub : Hub
{
    public async Task NotifyImageStored(int imageId, string sopInstanceUid)
    {
        await Clients.Others.SendAsync("ImageStored", imageId, sopInstanceUid);
    }

    public async Task NotifyStudyUpdated(int studyId, string studyInstanceUid)
    {
        await Clients.Others.SendAsync("StudyUpdated", studyId, studyInstanceUid);
    }

    public async Task NotifyDicomReceived(string callingAe, string sopInstanceUid)
    {
        await Clients.All.SendAsync("DicomReceived", callingAe, sopInstanceUid);
    }

    public async Task NotifyWorkstationStatus(string workstationId, string status)
    {
        await Clients.Others.SendAsync("WorkstationStatus", workstationId, status);
    }

    public override async Task OnConnectedAsync()
    {
        await Clients.Caller.SendAsync("Connected", Context.ConnectionId);
        await base.OnConnectedAsync();
    }
}
