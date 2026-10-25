using Dapper;
using Microsoft.Extensions.Logging;
using Puma.MDE.Trader.WebUI.Services.Data;
using System.Data;
using System.Text;

namespace Puma.MDE.Trader.WebUI.Services;

public class DapperPubRequestRepository : IPubRequestRepository
{
    private const string BaseSelectSql = @"
        SELECT 
            r.id AS Id,
            u.name AS UserName,
            r.market_data_key AS MarketDataKey,
            r.market_data_type AS MarketDataType,
            r.status AS Status,
            r.enqueued_at AS EnqueuedAt,
            r.finalized_at AS FinalizedAt,
            r.target AS Target,
            r.last_error AS LastError,
            r.attempts AS Attempts,
            r.payload_updated_at AS PayloadUpdatedAt,
            r.claimed_at AS ClaimedAt,
            r.claimed_by AS ClaimedBy
        FROM eqmx_pub_request r
        JOIN puma_mde_users u ON r.enqueued_by = u.ident ";

    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ILogger<DapperPubRequestRepository> _logger;

    public DapperPubRequestRepository(
        IDbConnectionFactory connectionFactory,
        ILogger<DapperPubRequestRepository> logger)
    {
        _connectionFactory = connectionFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PubRequestSummary>> GetTodayRequestsAsync(
        DateTime today, 
        string target = "MUREX", 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Querying all today's requests for target {Target} since {Today}", target, today);

        const string sql = BaseSelectSql + @"
            WHERE r.target = :target
              AND r.enqueued_at >= :today
            ORDER BY r.id ASC";

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, new { target, today }, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<PubRequestSummary>(command);
            return results.AsList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PubRequestSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query today's requests for target {Target}", target);
            throw;
        }
    }

    public async Task<IReadOnlyList<PubRequestSummary>> GetNewRequestsAfterIdAsync(
        long lastId, 
        DateTime today, 
        string target = "MUREX", 
        CancellationToken cancellationToken = default)
    {
        const string sql = BaseSelectSql + @"
            WHERE r.target = :target
              AND r.enqueued_at >= :today
              AND r.id > :lastId
            ORDER BY r.id ASC";

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, new { target, today, lastId }, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<PubRequestSummary>(command);
            return results.AsList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PubRequestSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query new requests after ID {LastId}", lastId);
            throw;
        }
    }

    public async Task<IReadOnlyList<PubRequestSummary>> GetRequestsByIdsAsync(
        IEnumerable<long> ids, 
        CancellationToken cancellationToken = default)
    {
        var idList = ids.Distinct().ToList();
        if (idList.Count == 0)
        {
            return Array.Empty<PubRequestSummary>();
        }

        const string sql = BaseSelectSql + @"
            WHERE r.id IN :idList";

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, new { idList }, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<PubRequestSummary>(command);
            return results.AsList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PubRequestSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query requests by IDs batch of {Count} items", idList.Count);
            throw;
        }
    }

    public async Task<IReadOnlyList<PubRequestSummary>> GetArchiveRequestsAsync(
        ArchivePubRequestFilterCriteria criteria, 
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Querying archive requests with filter {@Criteria}", criteria);

        var sqlBuilder = new StringBuilder(BaseSelectSql);
        sqlBuilder.Append(" WHERE r.target = 'MUREX' ");

        var dynamicParams = new DynamicParameters();

        // 1. Username filter
        if (criteria.SeeMineOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserName))
        {
            var cleanUsername = criteria.CurrentUserName.Trim();
            string? altUsername = null;
            if (cleanUsername.Contains('\\'))
            {
                var parts = cleanUsername.Split('\\', 2);
                altUsername = parts[1].Trim();
            }
            else if (cleanUsername.Contains('@'))
            {
                var parts = cleanUsername.Split('@', 2);
                altUsername = parts[0].Trim();
            }

            if (!string.IsNullOrEmpty(altUsername))
            {
                sqlBuilder.Append(" AND (UPPER(u.name) = UPPER(:username) OR UPPER(u.name) = UPPER(:altUsername)) ");
                dynamicParams.Add("username", cleanUsername);
                dynamicParams.Add("altUsername", altUsername);
            }
            else
            {
                sqlBuilder.Append(" AND UPPER(u.name) = UPPER(:username) ");
                dynamicParams.Add("username", cleanUsername);
            }
        }
        else if (!criteria.SeeMineOnly && !string.IsNullOrWhiteSpace(criteria.SpecificUserName))
        {
            sqlBuilder.Append(" AND UPPER(u.name) LIKE '%' || UPPER(:userSearch) || '%' ");
            dynamicParams.Add("userSearch", criteria.SpecificUserName.Trim());
        }

        // 2. Date filters on enqueued_at
        var (startDate, endDate) = criteria.GetEffectiveDateRange();
        if (startDate.HasValue)
        {
            sqlBuilder.Append(" AND r.enqueued_at >= :startDate ");
            dynamicParams.Add("startDate", startDate.Value);
        }
        if (endDate.HasValue)
        {
            sqlBuilder.Append(" AND r.enqueued_at <= :endDate ");
            dynamicParams.Add("endDate", endDate.Value);
        }

        // 3. Underlying (market_data_key)
        if (!string.IsNullOrWhiteSpace(criteria.Underlying))
        {
            sqlBuilder.Append(" AND UPPER(r.market_data_key) LIKE '%' || UPPER(:underlying) || '%' ");
            dynamicParams.Add("underlying", criteria.Underlying.Trim());
        }

        // 4. Market Data Type
        if (!string.IsNullOrWhiteSpace(criteria.MarketDataType) && !string.Equals(criteria.MarketDataType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            sqlBuilder.Append(" AND UPPER(r.market_data_type) = UPPER(:marketDataType) ");
            dynamicParams.Add("marketDataType", criteria.MarketDataType.Trim());
        }

        // 5. Status
        if (!string.IsNullOrWhiteSpace(criteria.Status) && !string.Equals(criteria.Status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            sqlBuilder.Append(" AND UPPER(r.status) = UPPER(:status) ");
            dynamicParams.Add("status", criteria.Status.Trim());
        }

        // 6. Technical User role
        if (!criteria.IsTechnicalUser)
        {
            sqlBuilder.Append(" AND u.is_technical_user = 0 ");
        }

        // 7. Ordering & Limit
        var maxRows = criteria.MaxRows > 0 ? criteria.MaxRows : 1000;
        sqlBuilder.Append(" ORDER BY r.finalized_at DESC NULLS FIRST, r.enqueued_at DESC ");
        sqlBuilder.Append(" FETCH FIRST :maxRows ROWS ONLY ");
        dynamicParams.Add("maxRows", maxRows);

        var sql = sqlBuilder.ToString();

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, dynamicParams, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<PubRequestSummary>(command);
            return results.AsList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PubRequestSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query archive requests with criteria {@Criteria}", criteria);
            throw;
        }
    }

    public async Task<IReadOnlyList<PubRequestSummary>> GetLatestRequestsAsync(
        PubRequestFilterCriteria criteria,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting latest requests with filter {@Criteria}", criteria);

        var sqlBuilder = new StringBuilder(BaseSelectSql);
        sqlBuilder.Append(" WHERE r.target = 'MUREX' ");

        var dynamicParams = new DynamicParameters();

        // 1. Username filter
        if (criteria.SeeMineOnly && !string.IsNullOrWhiteSpace(criteria.CurrentUserName))
        {
            var cleanUsername = criteria.CurrentUserName.Trim();
            string? altUsername = null;
            if (cleanUsername.Contains('\\'))
            {
                var parts = cleanUsername.Split('\\', 2);
                altUsername = parts[1].Trim();
            }
            else if (cleanUsername.Contains('@'))
            {
                var parts = cleanUsername.Split('@', 2);
                altUsername = parts[0].Trim();
            }

            if (!string.IsNullOrEmpty(altUsername))
            {
                sqlBuilder.Append(" AND (UPPER(u.name) = UPPER(:username) OR UPPER(u.name) = UPPER(:altUsername)) ");
                dynamicParams.Add("username", cleanUsername);
                dynamicParams.Add("altUsername", altUsername);
            }
            else
            {
                sqlBuilder.Append(" AND UPPER(u.name) = UPPER(:username) ");
                dynamicParams.Add("username", cleanUsername);
            }
        }
        else if (!criteria.SeeMineOnly && !string.IsNullOrWhiteSpace(criteria.SpecificUserName))
        {
            sqlBuilder.Append(" AND UPPER(u.name) LIKE '%' || UPPER(:userSearch) || '%' ");
            dynamicParams.Add("userSearch", criteria.SpecificUserName.Trim());
        }

        // 2. Date filters on enqueued_at
        var (startDate, endDate) = criteria.GetEffectiveDateRange();
        if (startDate.HasValue)
        {
            sqlBuilder.Append(" AND r.enqueued_at >= :startDate ");
            dynamicParams.Add("startDate", startDate.Value);
        }
        if (endDate.HasValue)
        {
            sqlBuilder.Append(" AND r.enqueued_at <= :endDate ");
            dynamicParams.Add("endDate", endDate.Value);
        }

        // 3. Underlying (market_data_key)
        if (!string.IsNullOrWhiteSpace(criteria.Underlying))
        {
            sqlBuilder.Append(" AND UPPER(r.market_data_key) LIKE '%' || UPPER(:underlying) || '%' ");
            dynamicParams.Add("underlying", criteria.Underlying.Trim());
        }

        // 4. Market Data Type
        if (!string.IsNullOrWhiteSpace(criteria.MarketDataType) && !string.Equals(criteria.MarketDataType, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            sqlBuilder.Append(" AND UPPER(r.market_data_type) = UPPER(:marketDataType) ");
            dynamicParams.Add("marketDataType", criteria.MarketDataType.Trim());
        }

        // 5. Status
        if (!string.IsNullOrWhiteSpace(criteria.Status) && !string.Equals(criteria.Status, "ALL", StringComparison.OrdinalIgnoreCase))
        {
            sqlBuilder.Append(" AND UPPER(r.status) = UPPER(:status) ");
            dynamicParams.Add("status", criteria.Status.Trim());
        }

        // 6. Technical User role
        if (!criteria.IsTechnicalUser)
        {
            sqlBuilder.Append(" AND u.is_technical_user = 0 ");
        }

        // 7. Ordering & Limit
        var maxRows = criteria.MaxRows > 0 ? criteria.MaxRows : 500;
        sqlBuilder.Append(" ORDER BY r.finalized_at DESC NULLS FIRST, r.enqueued_at DESC ");
        sqlBuilder.Append(" FETCH FIRST :maxRows ROWS ONLY ");
        dynamicParams.Add("maxRows", maxRows);

        var sql = sqlBuilder.ToString();

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, dynamicParams, cancellationToken: cancellationToken);
            var results = await connection.QueryAsync<PubRequestSummary>(command);
            return results.AsList();
        }
        catch (OperationCanceledException)
        {
            return Array.Empty<PubRequestSummary>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query eqmx_pub_request with criteria {@Criteria}", criteria);
            throw;
        }
    }

    public async Task<PubRequestDetail?> GetRequestDetailsByIdAsync(long id, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                r.id AS Id,
                u.name AS UserName,
                r.market_data_key AS MarketDataKey,
                r.market_data_type AS MarketDataType,
                r.status AS Status,
                r.enqueued_at AS EnqueuedAt,
                r.finalized_at AS FinalizedAt,
                mreq.content_type AS RequestFormat,
                mreq.content AS RequestContent,
                mresp.content_type AS ResponseFormat,
                mresp.content AS ResponseContent,
                ma.error_message AS ErrorMessage,
                r.claimed_by AS ClaimedBy
            FROM eqmx_pub_request r
            LEFT JOIN puma_mde_users u ON r.enqueued_by = u.ident
            LEFT JOIN eqmx_pub_request_payload p ON (r.id = p.request_id OR r.payload_id = p.id)
            LEFT JOIN eqmx_pub_message_log mreq ON (mreq.message_id = r.message_id AND mreq.incoming = 0)
            LEFT JOIN eqmx_pub_message_log mresp ON (mresp.message_id = r.message_id AND mresp.incoming = 1)
            LEFT JOIN eqmx_pub_message_attempt ma ON r.message_id = ma.message_id
            WHERE r.id = :id
            ORDER BY ma.id DESC NULLS LAST
            FETCH FIRST 1 ROW ONLY";

        try
        {
            using var connection = _connectionFactory.CreateConnection();
            if (connection.State != ConnectionState.Open)
                connection.Open();

            var command = new CommandDefinition(sql, new { id }, cancellationToken: cancellationToken);
            var result = await connection.QuerySingleOrDefaultAsync<PubRequestDetail>(command);

            if (result != null)
            {
                result.UserFriendlyError = PubRequestErrorTranslator.Translate(result.ErrorMessage, result.Status, result.MarketDataKey);
                result.FormattedPayload = result.RequestContent ?? "";
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query detailed publication request for ID {RequestId}", id);
            throw;
        }
    }

    public async Task<IReadOnlyList<string>> GetDistinctMarketDataTypesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new[] { "CALIBRATION_SV", "CALIBRATION_SVLV", "CALIBRATION_SVSIR", "DIVIDEND", "GMP_DATA_SV", "GMP_DATA_SVLV_LV", "GMP_DATA_SVLV_SV", "GMP_DATA_SVSIR", "MATURITY_SET", "REPO_CURVE", "VOLATILITY_SURFACE" });
    }

    public async Task<IReadOnlyList<string>> GetDistinctStatusesAsync(CancellationToken cancellationToken = default)
    {
        return await Task.FromResult(new[] { "PENDING", "CLAIMED", "BATCHED", "PUBLISHED", "FAILED", "SUPERSEDED", "SKIPPED" });
    }
}
