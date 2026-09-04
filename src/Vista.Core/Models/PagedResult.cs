using System.Collections.Generic;

namespace Vista.Core.Models
{
    /// <summary>
    /// 分页结果容器。所有"列表型"读取接口（时间线、评论、搜索、粉丝列表）统一返回此结构。
    /// 翻页靠 Cursor（游标），不用页码——主流平台（微博、小红书）实际都是游标分页。
    /// </summary>
    /// <typeparam name="T">条目类型</typeparam>
    public sealed class PagedResult<T>
    {
        /// <summary>当前页条目。</summary>
        public IReadOnlyList<T> Items { get; }

        /// <summary>
        /// 下一页游标；为 null 表示已到末尾。直接传回原请求即可取下一页。
        /// </summary>
        public string NextCursor { get; }

        /// <summary>是否还有更多（NextCursor 非空的便捷判断）。</summary>
        public bool HasMore => !string.IsNullOrEmpty(NextCursor);

        public PagedResult(IReadOnlyList<T> items, string nextCursor)
        {
            Items = items ?? new List<T>(0);
            NextCursor = nextCursor;
        }

        public static PagedResult<T> Empty => new PagedResult<T>(new List<T>(0), null);
    }
}
