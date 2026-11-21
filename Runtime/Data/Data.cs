using System;

namespace XO.RuntimeDatabase
{
    [Serializable]
    public struct Data<THash, TValue>
    {
        public THash key;
        public TValue value;
    }
}