namespace FSH.WebApi.Application.Catalog.Brands;

public class SearchBrandsRequest : PaginationFilter, IRequest<PaginationResponse<BrandDto>>
{
}

public class BrandsBySearchRequestSpec : EntitiesByPaginationFilterSpec<Brand, BrandDto>
{
    public BrandsBySearchRequestSpec(SearchBrandsRequest request)
        : base(request)
    {
        if (!request.HasOrderBy())
        {
            Query.OrderBy(c => c.Name);
        }
    }
}

public class SearchBrandsRequestHandler : IRequestHandler<SearchBrandsRequest, PaginationResponse<BrandDto>>
{
    private readonly IReadRepository<Brand> _repository;

    public SearchBrandsRequestHandler(IReadRepository<Brand> repository) => _repository = repository;

    public async Task<PaginationResponse<BrandDto>> Handle(SearchBrandsRequest request, CancellationToken cancellationToken)
    {
        var spec = new BrandsBySearchRequestSpec(request);

        var list = await _repository.ListAsync(spec, cancellationToken);
        int count = await _repository.CountAsync(spec, cancellationToken);

        return new PaginationResponse<BrandDto>(list, count, request.PageNumber, request.PageSize);
    }
}