using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Bencodex;
using Bencodex.Types;
using GraphQL;
using GraphQL.Execution;
using Libplanet;
using Libplanet.Action;
using Libplanet.Assets;
using Libplanet.Blockchain;
using Libplanet.Blockchain.Policies;
using Libplanet.Crypto;
using Libplanet.Store;
using Libplanet.Store.Trie;
using Nekoyume;
using Nekoyume.Action;
using Nekoyume.Helper;
using Nekoyume.Model.State;
using NineChronicles.Headless.GraphTypes;
using Xunit;
using static NineChronicles.Headless.Tests.GraphQLTestUtils;
using NCAction = Libplanet.Action.PolymorphicAction<Nekoyume.Action.ActionBase>;

namespace NineChronicles.Headless.Tests.GraphTypes
{
    public class ActionQueryTest
    {
        private readonly Codec _codec;
        private readonly StandaloneContext _standaloneContext;

        public ActionQueryTest()
        {
            _codec = new Codec();
            var store = new DefaultStore(null);
            var stateStore = new TrieStateStore(new DefaultKeyValueStore(null));
            var minerPrivateKey = new PrivateKey();
            var genesisBlock = BlockChain<NCAction>.MakeGenesisBlock(
                HashAlgorithmType.Of<SHA256>(),
                new PolymorphicAction<ActionBase>[]
                {
                    new InitializeStates(
                        rankingState: new RankingState0(),
                        shopState: new ShopState(),
                        gameConfigState: new GameConfigState(),
                        redeemCodeState: new RedeemCodeState(Bencodex.Types.Dictionary.Empty
                            .Add("address", RedeemCodeState.Address.Serialize())
                            .Add("map", Bencodex.Types.Dictionary.Empty)
                        ),
                        adminAddressState: new AdminState(new PrivateKey().ToAddress(), 1500000),
                        activatedAccountsState: new ActivatedAccountsState(),
                        goldCurrencyState: new GoldCurrencyState(new Currency("NCG", 2, minerPrivateKey.ToAddress())),
                        goldDistributions: Array.Empty<GoldDistribution>(),
                        tableSheets: new Dictionary<string, string>(),
                        pendingActivationStates: new PendingActivationState[]{ }
                    ),
                },
                privateKey: minerPrivateKey
            );
            var blockchain = new BlockChain<PolymorphicAction<ActionBase>>(
                new BlockPolicy<PolymorphicAction<ActionBase>>(),
                new VolatileStagePolicy<PolymorphicAction<ActionBase>>(),
                store,
                stateStore,
                genesisBlock);
            _standaloneContext = new StandaloneContext
            {
                BlockChain = blockchain,
                Store = store,
            };
        }

        [Theory]
        [ClassData(typeof(StakeFixture))]
        public async Task Stake(BigInteger amount)
        {
            string query = $@"
            {{
                stake(amount: {amount})
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            NCAction action = new Stake(amount);
            var expected = new Dictionary<string, object>()
            {
                ["stake"] = ByteUtil.Hex(_codec.Encode(action.PlainValue)),
            };
            Assert.Equal(expected, data);
        }

        [Fact]
        public async Task ClaimStakeReward()
        {
            var avatarAddress = new PrivateKey().ToAddress();
            string query = $@"
            {{
                claimStakeReward(avatarAddress: ""{avatarAddress.ToString()}"")
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string) data["claimStakeReward"]));
            Assert.IsType<Dictionary>(plainValue);
            var dictionary = (Dictionary) plainValue;
            Assert.IsType<ClaimStakeReward>(DeserializeNCAction(dictionary).InnerAction);
        }

        [Fact]
        public async Task MigrateMonsterCollection()
        {
            var avatarAddress = new PrivateKey().ToAddress();
            string query = $@"
            {{
                migrateMonsterCollection(avatarAddress: ""{avatarAddress.ToString()}"")
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode)queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string)data["migrateMonsterCollection"]));
            var dictionary = Assert.IsType<Dictionary>(plainValue);
            var action = Assert.IsType<MigrateMonsterCollection>(DeserializeNCAction(dictionary).InnerAction);
            Assert.Equal(avatarAddress, action.AvatarAddress);
        }

        private class StakeFixture : IEnumerable<object[]>
        {
            private readonly List<object[]> _data = new List<object[]>
            {
                new object[]
                {
                    new BigInteger(1),
                },
                new object[]
                {
                    new BigInteger(100),
                },
            };

            public IEnumerator<object[]> GetEnumerator() => _data.GetEnumerator();

            IEnumerator IEnumerable.GetEnumerator() => _data.GetEnumerator();
        }
        [Theory]
        [InlineData("false", false)]
        [InlineData("true", true)]
        [InlineData(null, false)]
        public async Task Grinding(string chargeApValue, bool chargeAp)
        {
            var avatarAddress = new PrivateKey().ToAddress();
            var equipmentId = Guid.NewGuid();
            string queryArgs = $"avatarAddress: \"{avatarAddress.ToString()}\", equipmentIds: [{string.Format($"\"{equipmentId}\"")}]";
            if (!string.IsNullOrEmpty(chargeApValue))
            {
                queryArgs += $", chargeAp: {chargeApValue}";
            }
            string query = $@"
            {{
                grinding({queryArgs})
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string) data["grinding"]));
            Assert.IsType<Dictionary>(plainValue);
            var polymorphicAction = DeserializeNCAction(plainValue);
            var action = Assert.IsType<Grinding>(polymorphicAction.InnerAction);

            Assert.Equal(avatarAddress, action.AvatarAddress);
            Assert.Single(action.EquipmentIds);
            Assert.Equal(equipmentId, action.EquipmentIds.First());
            Assert.Equal(chargeAp, action.ChargeAp);
        }

        [Fact]
        public async Task UnlockEquipmentRecipe()
        {
            var avatarAddress = new PrivateKey().ToAddress();
            string query = $@"
            {{
                unlockEquipmentRecipe(avatarAddress: ""{avatarAddress.ToString()}"", recipeIds: [2, 3])
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string) data["unlockEquipmentRecipe"]));
            Assert.IsType<Dictionary>(plainValue);
            var polymorphicAction = DeserializeNCAction(plainValue);
            var action = Assert.IsType<UnlockEquipmentRecipe>(polymorphicAction.InnerAction);

            Assert.Equal(avatarAddress, action.AvatarAddress);
            Assert.Equal(
                new List<int>
                {
                    2,
                    3,
                },
                action.RecipeIds
            );
        }

        [Fact]
        public async Task UnlockWorld()
        {
            var avatarAddress = new PrivateKey().ToAddress();
            string query = $@"
            {{
                unlockWorld(avatarAddress: ""{avatarAddress.ToString()}"", worldIds: [2, 3])
            }}";

            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>)((ExecutionNode) queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string) data["unlockWorld"]));
            Assert.IsType<Dictionary>(plainValue);
            var polymorphicAction = DeserializeNCAction(plainValue);
            var action = Assert.IsType<UnlockWorld>(polymorphicAction.InnerAction);

            Assert.Equal(avatarAddress, action.AvatarAddress);
            Assert.Equal(
                new List<int>
                {
                    2,
                    3,
                },
                action.WorldIds
            );
        }

        [Theory]
        [InlineData("NCG", true)]
        [InlineData("NCG", false)]
        [InlineData("CRYSTAL", true)]
        [InlineData("CRYSTAL", false)]
        public async Task TransferAsset(string currencyType, bool memo)
        {
            var recipient = new PrivateKey().ToAddress();
            var sender = new PrivateKey().ToAddress();
            var args = $"recipient: \"{recipient}\", sender: \"{sender}\", amount: \"17.5\", currency: {currencyType}";
            if (memo)
            {
                args += ", memo: \"memo\"";
            }
            var query = $"{{ transferAsset({args}) }}";
            var queryResult = await ExecuteQueryAsync<ActionQuery>(query, standaloneContext: _standaloneContext);
            var data = (Dictionary<string, object>) ((ExecutionNode) queryResult.Data!).ToValue()!;
            var plainValue = _codec.Decode(ByteUtil.ParseHex((string) data["transferAsset"]));
            Assert.IsType<Dictionary>(plainValue);
            var polymorphicAction = DeserializeNCAction(plainValue);
            var action = Assert.IsType<TransferAsset>(polymorphicAction.InnerAction);
            var rawState = _standaloneContext.BlockChain!.GetState(Addresses.GoldCurrency);
            var goldCurrencyState = new GoldCurrencyState((Dictionary) rawState);
            Currency currency = currencyType == "NCG" ? goldCurrencyState.Currency : CrystalCalculator.CRYSTAL;

            Assert.Equal(recipient, action.Recipient);
            Assert.Equal(sender, action.Sender);
            Assert.Equal(FungibleAssetValue.Parse(currency, "17.5"), action.Amount);
            if (memo)
            {
                Assert.Equal("memo", action.Memo);
            }
            else
            {
                Assert.Null(action.Memo);
            }
        }

        private NCAction DeserializeNCAction(IValue value)
        {
#pragma warning disable CS0612
            NCAction action = new NCAction();
#pragma warning restore CS0612
            action.LoadPlainValue(value);
            return action;
        }
    }
}
