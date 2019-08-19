using Neo.SmartContract.Framework;
using Neo.SmartContract.Framework.Services.Neo;
using Neo.SmartContract.Framework.Services.System;
using Helper = Neo.SmartContract.Framework.Helper;
using System;
using System.ComponentModel;
using System.Numerics;


namespace NFTMarket
{
    public class SaleMulInfo
    {
        public BigInteger saleid;   // 挂单ID
        public BigInteger price;    // 打包交易的总价格
        public BigInteger count;
        public BigInteger[] ids;
    }

    public class NFTMarket : SmartContract
    {
        [DisplayName("mulsale")] // nftx
        public static event Action<byte[], SaleMulInfo> MulSaled;
        [DisplayName("canclemulsale")] // nftx
        public static event Action<byte[], BigInteger> CancleMulSaled;
        [DisplayName("nftmarketbuy")]
        public static event Action<byte[], BigInteger> NftMarketBuyed;
        [DisplayName("mulsaleerr")]
        public static event Action<byte[], BigInteger> MulSaleErr;
        [DisplayName("marketbuyerr")]
        public static event Action<byte[], BigInteger, BigInteger> MarketBuyErr;
        [DisplayName("canclemulsaleerr")]
        public static event Action<byte[], BigInteger, BigInteger> CancleMulSaleErr;

        private static readonly byte[] Owner = "AeMjpHfAbe4DZ3Qpvzem1aiAhz2FwDJYmH".ToScriptHash(); //Owner Address

        [Appcall("4c9e54f6481888065ba5172269d355ae44fc3395")]
        public static extern bool Nep5transfer(string method, object[] arr);

        [Appcall("f8e64aef696aff7c4d116a85839a608e55cd1abe")]
        public static extern bool NFTtransfer(string method, object[] arr);


        public static object Main(string method, object[] args)
        {
            if (Runtime.Trigger == TriggerType.Verification)
            {
                return Runtime.CheckWitness(Owner);
            }
            else if (Runtime.Trigger == TriggerType.Application)
            {
                var callscript = ExecutionEngine.CallingScriptHash;

                if (method == "mulsale")
                {
                    return MulSale(args);
                }

                if (method == "getmulsale")
                {
                    if (args.Length < 1) return false;
                    return GetMulSale((byte[])args[0]);
                }

                if (method == "canclesale")
                {
                    if (args.Length < 2) return false;
                    return CancleSale((byte[])args[0], (BigInteger)args[1]);
                }

                if (method == "buyfromother")
                {
                    if (args.Length < 3) return false;
                    return BuyFromOther((byte[])args[0], (byte[])args[1], (BigInteger)args[2]);
                }

                if (method == "upgrade")
                {
                    return Upgrade(args);
                }
            }
            return false;
        }

        

        private static bool IsPayable(byte[] to)
        {
            var c = Blockchain.GetContract(to);
            return c == null || c.IsPayable;
        }

        // 生成自增的挂单ID
        private static BigInteger _createSaleID()
        {
            StorageMap saleID = Storage.CurrentContext.CreateMap(nameof(saleID));
            byte[] sid = saleID.Get("saleID");
            if (sid == null || sid.Length == 0)
            {
                BigInteger id = 2_000_000_400;
                saleID.Put("saleID", id);
                return id;
            }
            else
            {

                BigInteger id = sid.AsBigInteger();
                id = id + 1;
                saleID.Put("saleID", id);
                return id;
            }
        }

        // 对几个资产进行打包,挂单售卖
        private static bool MulSale(object[] args)
        {
            if (args.Length < 3) return false;
            byte[] account = (byte[])args[0];
            BigInteger price = (BigInteger)args[1];
            BigInteger amount = (BigInteger)args[2];
            //Check parameters
            if (account.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");
            if (amount <= 0 || amount > 9 || price < 0)
                throw new InvalidOperationException("The parameter amount MUST be greater than 0.");
            if (args.Length < 3 + amount) return false;

            if (!Runtime.CheckWitness(account))  //0.2
                return false;

            object[] objs = new object[12];
            objs[0] = account;
            objs[1] = ExecutionEngine.ExecutingScriptHash;
            objs[2] = amount;
            for (int i = 0; i < amount; i ++ )
            {
                objs[3 + i] = args[3 + i];
            }
            // -----------调用EasyNFT 转移资产
            if (NFTtransfer("transfer", objs) == false)
            {
                MulSaleErr(account, 10015);
                throw new Exception("NFTtransfer Fail！");
            }
            //----------

            BigInteger sid = _createSaleID();
            SaleMulInfo info = new SaleMulInfo();
            info.saleid = sid;
            info.price = price;
            info.ids = new BigInteger[9];
            info.count = amount;
            for (int i = 0; i < amount; i++)
            {
                info.ids[i] = (BigInteger)args[3 + i];
            }

            // 将售卖信息插入售卖表saleMul 中
            StorageMap saleMul = Storage.CurrentContext.CreateMap(nameof(saleMul));
            byte[] smFrom = saleMul.Get(account);
            Map<BigInteger, SaleMulInfo> items;
            if (smFrom == null || smFrom.Length == 0)
            {
                items = new Map<BigInteger, SaleMulInfo>();
            }
            else
            {
                items = Helper.Deserialize(smFrom) as Map<BigInteger, SaleMulInfo>;
            }

            items[sid] = info;

            saleMul.Put(account, Helper.Serialize(items));
            MulSaled(account, info);

            return true;
        }

        // 获取某账户名下所有售卖挂单
        private static Map<BigInteger, SaleMulInfo> GetMulSale(byte[] account)
        {
            StorageMap saleMul = Storage.CurrentContext.CreateMap(nameof(saleMul));
            byte[] smFrom = saleMul.Get(account);
            Map<BigInteger, SaleMulInfo> items;
            if (smFrom == null || smFrom.Length == 0)
            {
                items = new Map<BigInteger, SaleMulInfo>();
            }
            else
            {
                items = Helper.Deserialize(smFrom) as Map<BigInteger, SaleMulInfo>;
            }
            return items;
        }


        // 根据其他账户的售卖挂单，进行交易
        private static bool CancleSale(byte[] from, BigInteger sid)
        {
            //Check parameters
            if (from.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");

            if (!Runtime.CheckWitness(from))  //0.2
                return false;

            StorageMap saleMul = Storage.CurrentContext.CreateMap(nameof(saleMul));
            byte[] smFrom = saleMul.Get(from);
            // 如果from名下没有任何售卖单返回错误
            if (smFrom == null || smFrom.Length == 0)
            {
                CancleMulSaleErr(from, sid, 10013);
                return false;
            }

            Map<BigInteger, SaleMulInfo> sitems = Helper.Deserialize(smFrom) as Map<BigInteger, SaleMulInfo>;
            // 如果from名下没有该单号返回错误
            if (!sitems.HasKey(sid))
            {
                CancleMulSaleErr(from, sid, 10013);
                return false;
            }

            SaleMulInfo s = sitems[sid];
            object[] objs = new object[12];
            objs[0] = ExecutionEngine.ExecutingScriptHash;
            objs[1] = from;
            objs[2] = s.count;
            for (int i = 0; i < s.count; i ++)
            {
                objs[i + 3] = s.ids[i];
            }
           
            // -----------调用EasyNFT 转移资产
            if (NFTtransfer("transfer", objs) == false)
            {
                throw new Exception("NFTtransfer Fail！");
            }
            //----------

            // 删除该挂单
            sitems.Remove(sid);
            saleMul.Put(from, Helper.Serialize(sitems));
            CancleMulSaled(from, sid);
            return true;
        }

        // 根据其他账户的售卖挂单，进行交易
        private static bool BuyFromOther(byte[] saler, byte[] buyer, BigInteger sid)
        {
            //Check parameters
            if (buyer.Length != 20)
                throw new InvalidOperationException("The parameters from and to SHOULD be 20-byte addresses.");

            if (!IsPayable(buyer))
                return false;
            if (!Runtime.CheckWitness(buyer))  //0.2
                return false;

            StorageMap saleMul = Storage.CurrentContext.CreateMap(nameof(saleMul));
            byte[] smFrom = saleMul.Get(saler);
            // 如果from名下没有任何售卖单返回错误
            if (smFrom == null || smFrom.Length == 0)
            {
                MarketBuyErr(buyer, sid, 10013);
                return false;
            }

            Map<BigInteger, SaleMulInfo> sitems = Helper.Deserialize(smFrom) as Map<BigInteger, SaleMulInfo>;
            // 如果from名下没有该单号返回错误
            if (!sitems.HasKey(sid))
            {
                MarketBuyErr(buyer, sid, 10013);
                return false;
            }
            SaleMulInfo s = sitems[sid];

            // -----------使用nep5购买
            if (Nep5transfer("transfer", new object[] { buyer, saler, s.price }) == false)
            {
                MarketBuyErr(buyer, sid, 10001);
                throw new Exception("NEP5transfer Fail！");
            }
            //----------

            object[] objs = new object[12];
            objs[0] = ExecutionEngine.ExecutingScriptHash;
            objs[1] = buyer;
            objs[2] = s.count;
            for (int i = 0; i < s.count; i++)
            {
                objs[i + 3] = s.ids[i];
            }
            // -----------调用EasyNFT 转移资产
            if (NFTtransfer("transfer", objs) == false)
            {
                throw new Exception("NFTtransfer Fail！");
            }

            sitems.Remove(sid);
            saleMul.Put(saler, Helper.Serialize(sitems));
            NftMarketBuyed(buyer, sid);
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
            string name = "NFTMarket";
            string version = "1";
            string author = "Fast";
            string email = "0";
            string description = "NFTMarket";

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
