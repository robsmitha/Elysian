using CapitolSharp.Congress;
using CapitolSharp.Congress.Bills;
using CapitolSharp.Congress.Congresses;
using CapitolSharp.Congress.Enums;
using Elysian.Application.Extensions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace Elysian
{
    public class CongressFunctions(ILogger<CongressFunctions> logger, CapitolSharpCongress congressClient)
    {
        [Function("CongressGetBills")]
        public async Task<HttpResponseData> GetBills([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var offset = int.TryParse(req.Query["offset"], out var intOffset) ? intOffset : 0;
            var limit = int.TryParse(req.Query["limit"], out var intLimit) ? intLimit : 20;
            var fromDateTime = DateTime.TryParse(req.Query["fromDateTime"], out var dtFromDateTime) ? dtFromDateTime : (DateTime?)null;
            var toDateTime = DateTime.TryParse(req.Query["toDateTime"], out var dtToDateTime) ? dtToDateTime : (DateTime?)null;
            var sort = req.Query["sort"] ?? "updateDate";
            var direction = !string.IsNullOrEmpty(req.Query["direction"]) && req.TryGetEnumValue<SortByDirection>("direction", out var enumSortDirection) ? enumSortDirection : SortByDirection.desc;

            var bills = await congressClient.SendAsync(new BillListAllRequest
            {
                Offset = offset,
                Limit = limit,
                FromDateTime = fromDateTime,
                ToDateTime = toDateTime,
                Sort = sort,
                Direction = direction
            });
            return await req.WriteJsonResponseAsync(bills);
        }

        [Function("CongressGetBill")]
        public async Task<HttpResponseData> GetBill([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var congress = int.TryParse(req.Query["congress"], out var intCongress) ? intCongress : 0;
            var billType = !string.IsNullOrEmpty(req.Query["billType"]) && req.TryGetEnumValue<BillType>("billType", out var enumBillType) ? enumBillType : BillType.hr;
            var billNumber = int.TryParse(req.Query["billNumber"], out var intBillNumber) ? intBillNumber : 0;

            var billDetails = await congressClient.SendAsync(new BillDetailsRequest
            {
                Congress = congress,
                BillType = billType,
                BillNumber = billNumber
            });

            var billActions = await congressClient.SendAsync(new BillActionsRequest
            {
                Congress = congress,
                BillType = billType,
                BillNumber = billNumber
            });

            var billAmendments = await congressClient.SendAsync(new BillAmendmentsRequest
            {
                Congress = congress,
                BillType = billType,
                BillNumber = billNumber
            });

            var billCosponsors = await congressClient.SendAsync(new BillCosponsorsRequest
            {
                Congress = congress,
                BillType = billType,
                BillNumber = billNumber
            });

            return await req.WriteJsonResponseAsync(new
            {
                billDetails,
                billActions,
                billAmendments,
                billCosponsors
            });
        }

        [Function("CongressFeed")]
        public async Task<HttpResponseData> CongressFeed([HttpTrigger(AuthorizationLevel.Anonymous, "get")] HttpRequestData req)
        {
            var offset = int.TryParse(req.Query["offset"], out var intOffset) ? intOffset : 0;
            var currentCongress = await congressClient.SendAsync(new CongressCurrentListRequest());

            if (currentCongress?.Congress?.Number.HasValue != true)
            {
                throw new Exception();
            }

            var billList = await congressClient.SendAsync(new BillListByCongressRequest
            {
                Congress = currentCongress.Congress.Number.Value,
                Limit = 10,
                Offset = offset
            });

            return await req.WriteJsonResponseAsync(new
            {
                congressDetails = currentCongress.Congress,
                billList
            });
        }
    }
}
