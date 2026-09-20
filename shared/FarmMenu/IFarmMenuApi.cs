namespace FarmMenu;

// Only primitives and delegates cross SMAPI's API proxy. Menu code is compiled into
// each mod, so there is no extra core DLL or dependency on a particular feature.
public interface IFarmMenuApi
{
    void RegisterSection(string id, string parentId, string title, string description, int order);
    void RegisterAction(string sectionId, string id, Func<string> title, Func<string> description,
        Action execute, Func<bool> enabled, int order);
    void RegisterRefresh(string id, Action refresh);
    void Open(string sectionId);
    bool IsOpen();
    void Refresh();
}

public sealed class FarmMenuApi : IFarmMenuApi
{
    private readonly MenuController controller;
    internal FarmMenuApi(MenuController controller) => this.controller = controller;
    public void RegisterSection(string id, string parentId, string title, string description, int order)
        => controller.Sections[id] = new(id, parentId, title, description, order);
    public void RegisterAction(string sectionId, string id, Func<string> title, Func<string> description,
        Action execute, Func<bool> enabled, int order)
        => controller.Actions[id] = new(sectionId, id, title, description, execute, enabled, order);
    public void RegisterRefresh(string id, Action refresh) => controller.Refreshers[id] = refresh;
    public void Open(string sectionId) => controller.Open(sectionId);
    public bool IsOpen() => controller.ActiveMenu is HubPage;
    public void Refresh() => controller.ReloadAll();
}

internal sealed record MenuSection(string Id, string ParentId, string Title, string Description, int Order);
internal sealed record MenuAction(string SectionId, string Id, Func<string> Title, Func<string> Description,
    Action Execute, Func<bool> Enabled, int Order);
