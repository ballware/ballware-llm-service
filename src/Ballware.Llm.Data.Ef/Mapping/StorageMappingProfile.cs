using System;
using Mapster;
using Pgvector;

namespace Ballware.Llm.Data.Ef.Mapping;

class StorageMappingProfile : IRegister
{
    public void Register(TypeAdapterConfig config)
    {
        config.NewConfig<Public.Context, Persistables.Context>()
            .Ignore(dest => dest.Id!)
            .Map(dest => dest.Uuid, src => src.Id);
        
        config.NewConfig<Persistables.Context, Public.Context>()
            .Map(dest => dest.Id, src => src.Uuid);
        
        config.NewConfig<Public.Origin, Persistables.Origin>()
            .Ignore(dest => dest.Id!)
            .Map(dest => dest.Uuid, src => src.Id);
        
        config.NewConfig<Persistables.Origin, Public.Origin>()
            .Map(dest => dest.Id, src => src.Uuid);
        
        config.NewConfig<Public.Embedding, Persistables.Embedding>()
            .Ignore(dest => dest.Id!)
            .Map(dest => dest.Uuid, src => src.Id);
        
        config.NewConfig<Persistables.Embedding, Public.Embedding>()
            .Map(dest => dest.Id, src => src.Uuid);
        
        config.NewConfig<Public.Chunk, Persistables.Chunk>()
            .Ignore(dest => dest.Id!)
            .Map(dest => dest.Uuid, src => src.Id)
            .Map(dest => dest.Data, src => new Vector(new ReadOnlyMemory<float>(src.Data)));
        
        config.NewConfig<Persistables.Chunk, Public.Chunk>()
            .Map(dest => dest.Id, src => src.Uuid)
            .Map(dest => dest.Data, src => src.Data.ToArray());
        
        config.NewConfig<Public.Image, Persistables.Image>()
            .Ignore(dest => dest.Id!)
            .Map(dest => dest.Uuid, src => src.Id)
            .Map(dest => dest.Data, src => new Vector(new ReadOnlyMemory<float>(src.Data)));
        
        config.NewConfig<Persistables.Image, Public.Image>()
            .Map(dest => dest.Id, src => src.Uuid)
            .Map(dest => dest.Data, src => src.Data.ToArray());
    }
}
