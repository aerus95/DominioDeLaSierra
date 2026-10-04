using DominioDeLaSierra.Application.Products.GetProducts;

namespace DominioDeLaSierra.UnitTests;

public class GetProductsQueryTests
{
    [Fact]
    public void Default_page_size_is_twelve_and_the_maximum_stays_at_one_hundred()
    {
        var query = GetProductsQuery.Create();

        Assert.Equal(12, GetProductsQuery.DefaultPageSize);
        Assert.Equal(12, query.PageSize);
        Assert.Equal(1, query.Page);
        Assert.Equal(100, GetProductsQuery.MaxPageSize);
    }
}
