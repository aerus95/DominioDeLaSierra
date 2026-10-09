using System.Globalization;
using DominioDeLaSierra.Api.Security;
using DominioDeLaSierra.Application.Admin;
using DominioDeLaSierra.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DominioDeLaSierra.Api.Pages.Admin.Orders;

[Authorize(Policy = AdminAuthOptions.PanelPolicy)]
public sealed class OrderListModel(IAdminOrders orders) : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Number { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Customer { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Status { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Fulfillment { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Payment { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? From { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? To { get; set; }

    [BindProperty(SupportsGet = true, Name = "listPage")]
    public int PageNumber { get; set; } = 1;

    public AdminOrderListPage Results { get; private set; } = new([], 1, AdminOrderListQuery.PageSize, 0);

    public string? FilterMessage { get; private set; }

    public static string? SelectedAttr(bool selected) => selected ? "selected" : null;

    public bool CanWrite =>
        User.IsInRole(nameof(AdminRole.Administrator))
        || User.IsInRole(nameof(AdminRole.Manager));

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnGetRowsAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
        return Partial("_OrderFragment", this);
    }

    public string PageUrl(int page)
    {
        var pairs = new List<string>();
        Add(pairs, "number", Number);
        Add(pairs, "customer", Customer);
        Add(pairs, "status", Status);
        Add(pairs, "fulfillment", Fulfillment);
        Add(pairs, "payment", Payment);
        Add(pairs, "from", From);
        Add(pairs, "to", To);
        if (page > 1)
        {
            pairs.Add("listPage=" + page.ToString(CultureInfo.InvariantCulture));
        }

        return pairs.Count == 0 ? "/admin/orders" : "/admin/orders?" + string.Join("&", pairs);
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        var query = AdminOrderListQuery.Create(Number, Customer, Status, Fulfillment, Payment, From, To, PageNumber);
        FilterMessage = query.InvertedDates ? "La fecha inicial es posterior a la final." : null;
        Results = await orders.ListAsync(query, cancellationToken);
        PageNumber = Results.Page;
    }

    private static void Add(List<string> pairs, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            pairs.Add(name + "=" + Uri.EscapeDataString(value.Trim()));
        }
    }
}
