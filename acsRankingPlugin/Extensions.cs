using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace acsRankingPlugin
{
    static class Extensions
    {
        // key가 없어서 null인 것과 value가 null인 것은 구분하지 못하지만,
        // value가 null이 아닌 것이 확실한 상황에서는 이렇게 쓰는 게 편하다.
        public static TValue TryGetValue<TKey, TValue>(this Dictionary<TKey, TValue> dic, TKey key)
        {
            dic.TryGetValue(key, out var value);
            return value;
        }

        // 꺼내고 바로 삭제
        public static TValue Pop<TKey, TValue>(this Dictionary<TKey, TValue> dic, TKey key)
        {
            if (dic.TryGetValue(key, out var value))
            {
                dic.Remove(key);
            }
            return value;
        }

        public static string LaptimeFormat(this TimeSpan time)
        {
            return (time == TimeSpan.MaxValue) ? "oo:oo.ooo" : time.ToString("mm\\:ss\\.fff");
        }
    }
}
