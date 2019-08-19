using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;


namespace EasyNFT
{
    public class NftInfo
    {
        public BigInteger nftid;        // 发行资源的ID
        public BigInteger type;         // 发行资源的类型
        public BigInteger total_amount; // 预计发行资源的数量
        public BigInteger price;        // 发行的价格
        public BigInteger total_supply; // 已经卖出的数量
        public bool canbuy;             // 是否暂停发行
    }

    public class EasyNFT : SmartContract
    {
        [DisplayName("nfttransfer")] // nftx
        public static event Action<byte[], byte[], BigInteger> Transferred;
        [DisplayName("nftbuy")]
        public static event Action<byte[], BigInteger, BigInteger, BigInteger, BigInteger> Buyed;
        [DisplayName("burn")]
        public static event Action<byte[], BigInteger> Burnd;

        [DisplayName("nftbuyerr")]
        public static event Action<byte[], BigInteger, BigInteger> NftBuyErr;
        [DisplayName("transfererr")]
        public static event Action<byte[], byte[], BigInteger, BigInteger> TransferErr;

        private static readonly byte[] Owner = "AeMjpHfAbe4DZ3Qpvzem1aiAhz2FwDJYmH".ToScriptHash(); //Owner Address

        [Appcall("4c9e54f6481888065ba5172269d355ae44fc3395")]
        public static extern bool Nep5transfer(string method, object[] arr);


        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(Owner);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "deploy")
                {
                    if (args.Length < 4) return false;
                    return Deploy((BigInteger)args[0], (BigInteger)args[1], (BigInteger)args[2], (BigInteger)args[3]);
                }

                if (method == "nftsupplyinfo")
                {
                    if (args.Length < 1) return false;
                    return GetNftSupplyInfo((BigInteger)args[0]);
                }

                if (method == "balanceOf")
                {
                    if (args.Length < 1) return false;
                    return BalanceOf((byte[])args[0]);
                }

                if (method == "buy")
                {
                    if (args.Length < 2) return false;
                    return BuyOfficial((byte[])args[0], (BigInteger)args[1]);
                }

                if (method == "transfer")
                {
                    return Transfer(callscript, args);
                }

                if (method == "burn")
                {
                    return Burn(callscript, args);
                }

                if (method == "upgrade")
                {
                    return Upgrade(args);
                }
            }
            return false;
        }

        // 可以动态发行一个新的资产
        private static bool Deploy(BigInteger anftid, BigInteger atype, BigInteger atotal_amount, BigInteger aprice)
        {
            if (!Runtime.CheckWitness(Owner))
                return false;

            StorageMap nftinfo = Storage.CurrentContext.CreateMap(nameof(nftinfo));
            byte[] ni = nftinfo.Get(anftid.AsByteArray());
            if (ni != null && ni.Length != 0) return false;

            NftInfo info = new NftInfo
            {
                nftid = anftid,
                type = atype,
                total_amount = atotal_amount,
                price = aprice,
                total_supply = 0,
                canbuy = true
            };

            nftinfo.Put(anftid.AsByteArray(), Helper.Serialize(info));
            return true;
        }

        // 获取某资产的发行信息
        public static NftInfo GetNftSupplyInfo(BigInteger anftid)
        {
            NftInfo info = null;
            StorageMap nftinfo = Storage.CurrentContext.CreateMap(nameof(nftinfo));
            byte[] ni = nftinfo.Get(anftid.AsByteArray());
            if (ni != null && ni.Length != 0)
            {
                info = Helper.Deserialize(ni) as NftInfo;
            }
            return info;
        }

        // 返回某账户下的所有资产
        [DisplayName("balanceOf")]
        public static Map<BigInteger, BigInteger> BalanceOf(byte[] account)
        {
            //Map<BigInteger, BigInteger> m = new Map<BigInteger, BigInteger>();
            if (account.Length != 20)
                throw new InvalidOperationException("The parameter account SHOULD be 20-byte addresses.");
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            byte[] mp = asset.Get(account);
            if (mp != null && mp.Length != 0)
                return Helper.Deserialize(mp) as Map<BigInteger, BigInteger>;
            return null;
        }

        // 返回某资产的归属,由于gas费用太高,该接口未使用
        private static byte[] OwnerOf(BigInteger ID)
        {
            StorageMap attr = Storage.CurrentContext.CreateMap(nameof(attr));
            return attr.Get(ID.AsByteArray());
        }
        // 设置某资产的归属,由于gas费用太高,该接口未使用
        private static bool _saveToAttr(BigInteger ID, byte[] account)
        {
            StorageMap attr = Storage.CurrentContext.CreateMap(nameof(attr));
            attr.Put(ID.AsByteArray(), account);
            return true;
        }

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }

        // 生成资产的唯一ID
        private static BigInteger _createID(NftInfo info)
        {
            info.total_supply = info.total_supply + 1;
            BigInteger ID = info.nftid * 10_000_000_000 + (BigInteger)7 * 1_000_000_000 + info.total_supply;
            return ID;
        }

        // 将新购入的资产插入asset表中
        private static bool _saveToAsset(byte[] user, BigInteger ID)
        {
            // 为了节省gas，这里是否可以使用array或者list之类的容器
            // 如果把vlaue 定义为char，如Map<BigInteger, char>是否可以省gas
            Map<BigInteger, BigInteger> m = null;
            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            byte[] mp = asset.Get(user);
            if (mp != null && mp.Length != 0)
            {
                m = Helper.Deserialize(mp) as Map<BigInteger, BigInteger>;
            }
            else
            {
                m = new Map<BigInteger, BigInteger>();
            }

            m[ID] = 0;
            asset.Put(user, Helper.Serialize(m));
            return true;
        }

        // 从官方购买资产
        private static bool BuyOfficial(byte[] buyer, BigInteger anftid)
        {
            if (!Runtime.CheckWitness(buyer))
                return false;
            NftInfo info = null;
            StorageMap nftinfo = Storage.CurrentContext.CreateMap(nameof(nftinfo));
            byte[] ni = nftinfo.Get(anftid.AsByteArray());
            if (ni == null || ni.Length == 0)
                return false;

            info = Helper.Deserialize(ni) as NftInfo;
            if (info.total_supply == info.total_amount)
            {
                // 这里需要通知
                NftBuyErr(buyer, anftid, 10002);
                return false;
            }

            // -----------使用nep5购买
            if (Nep5transfer("transfer", new object[] { buyer, Owner, info.price }) == false)
            {
                NftBuyErr(buyer, anftid, 10001);
                return false;
            }
            //----------

            BigInteger ID = _createID(info);
            nftinfo.Put(anftid.AsByteArray(), Helper.Serialize(info));

            _saveToAsset(buyer, ID);
            Buyed(buyer, ID, info.nftid, info.type, info.price);
            Transferred(Owner, buyer, ID);
            return true;
        }

        // 转移自己的资产到别人的账户
        private static bool Transfer(byte[] callscript, object[] args)
        {
            if (args.Length < 3) return false;
            byte[] from = (byte[])args[0];
            byte[] to = (byte[])args[1];
            BigInteger amount = (BigInteger)args[2];
            if (args.Length < 3 + amount) return false;
            //Check parameters
            if (from.Length != 20 || to.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");
            if (amount <= 0)
                throw new InvalidOperationException("The parameter amount MUST be greater than 0.");
            if (!IsPayable(to))
                return false;
            if (!Runtime.CheckWitness(from) && from.AsBigInteger() != callscript.AsBigInteger())  //0.2
                return false;

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            byte[] mpfrom = asset.Get(from);
            byte[] mpto = asset.Get(to);
            if (mpfrom == null || mpfrom.Length == 0)
            {
                TransferErr(from, to, 0, 10015);
                return false;
            }
            Map<BigInteger, BigInteger> itemFrom = Helper.Deserialize(mpfrom) as Map<BigInteger, BigInteger>;
            Map<BigInteger, BigInteger> itemTo = null;
            if (mpto == null || mpto.Length == 0)
            {
                itemTo = new Map<BigInteger, BigInteger>();
            }
            else
            {
                itemTo = Helper.Deserialize(mpto) as Map<BigInteger, BigInteger>;
            }

            for (int i = 0; i < amount; i++)
            {
                BigInteger id = (BigInteger)args[i + 3];
                if (!itemFrom.HasKey(id))
                {
                    TransferErr(from, to, id, 10015);
                    return false;
                }
            }

            for (int i = 0; i < amount; i++)
            {
                BigInteger id = (BigInteger)args[i + 3];
                itemFrom.Remove(id);
                itemTo[id] = 0;
                Transferred(from, to, id);
            }
            // 修改asset表中发生交易的两个账户资产
            asset.Put(from, Helper.Serialize(itemFrom));
            asset.Put(to, Helper.Serialize(itemTo));

            return true;
        }

        // 转移自己的资产到别人的账户
        private static bool Burn(byte[] callscript, object[] args)
        {
            if (args.Length < 2) return false;
            byte[] from = (byte[])args[0];
            BigInteger id = (BigInteger)args[1];
            //Check parameters
            if (from.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");

            if (!Runtime.CheckWitness(from) && from.AsBigInteger() != callscript.AsBigInteger())  //0.2
                return false;

            StorageMap asset = Storage.CurrentContext.CreateMap(nameof(asset));
            byte[] mpfrom = asset.Get(from);
            if (mpfrom == null || mpfrom.Length == 0)
            {
                return false;
            }
            Map<BigInteger, BigInteger> itemFrom = Helper.Deserialize(mpfrom) as Map<BigInteger, BigInteger>;
            if (!itemFrom.HasKey(id))
            {
                return false;
            }

            itemFrom.Remove(id);
            asset.Put(from, Helper.Serialize(itemFrom));
            Burnd(from, id);
            return true;
        }

        #region 升级合约,耗费490,仅限管理员
        private static bool Upgrade(object[] args)
        {
            //不是管理员 不能操作
            if (!Runtime.CheckWitness(Owner))
                return false;

            if (args.Length != 1 && args.Length != 9)
                return false;

            byte[] script = Blockchain.GetContract(ExecutionEngine.ExecutingScriptHash).Script;
            byte[] new_script = (byte[])args[0];
            //如果传入的脚本一样 不继续操作
            if (script == new_script)
                return false;

            byte[] parameter_list = new byte[] { 0x07, 0x10 };
            byte return_type = 0x05;
            ContractPropertyState need_storage = (ContractPropertyState)(object)07;
            string name = "EasyNFT";
            string version = "1";
            string author = "Fast";
            string email = "0";
            string description = "EasyNFT";

            if (args.Length == 9)
            {
                parameter_list = (byte[])args[1];
                return_type = (byte)args[2];
                need_storage = (ContractPropertyState)args[3];
                name = (string)args[4];
                version = (string)args[5];
                author = (string)args[6];
                email = (string)args[7];
                description = (string)args[8];
            }
            Contract.Migrate(new_script, parameter_list, return_type, need_storage, name, version, author, email, description);
            return true;
        }
        #endregion
    }
}
