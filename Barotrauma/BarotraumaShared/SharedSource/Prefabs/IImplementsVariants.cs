using System.Linq;

namespace Barotrauma
{
	public interface IImplementsVariants<T> : IImplementsInherit<T> where T : Prefab{
		public void ApplyInherit();
		public Identifier VariantOf
		{
			get
			{
				var beforeOverrideInherit = InheritHistory.SkipWhile(p => p.Identifier == (this as T).Identifier);
				if(beforeOverrideInherit.Any()){
					return beforeOverrideInherit.First().Identifier;
				}
				else{
					return Identifier.Empty;
				}
			}
		}
	}
}
