#pragma once
#include <glm/glm.hpp>
#include <glm/gtc/packing.hpp>
#include <algorithm>
#include <cmath>
#include <cstdlib>
#include <cstdint>
#include <fstream>
#include <iostream>
#include <iterator>
#include <memory>
#include <stdexcept>
#include <vector>
using float2=glm::vec2;using float3=glm::vec3;using float4=glm::vec4;using int2=glm::ivec2;using bool2=glm::bvec2;using bool4=glm::bvec4;
using glm::dot;using glm::sqrt;using glm::roundEven;using glm::abs;using glm::min;using glm::max;using glm::clamp;using glm::step;
inline float4 SMAA_RT_METRICS;
inline int samplerPrecisionBits=8;
constexpr int samplerLinear=1,samplerPoint=0;
#define SMAA_LINEAR_SAMPLER samplerLinear
#define SMAA_POINT_SAMPLER samplerPoint
// A translated swizzle is a value; writes explicitly scatter without aliasing.
template<int... I,typename T>auto get(const T& value) {return glm::vec<sizeof...(I),typename T::value_type,glm::defaultp>(value[I]...);}
template<int... I,typename T,typename V>void set(T& target,const V& value) {int j=0;((target[I]=value[j++]),...);}
template<typename T>auto saturate(T value){return glm::clamp(value,0.f,1.f);}
template<typename A,typename B,typename C>auto mad(A a,B b,C c){return a*b+c;}
template<typename A,typename B,typename C>auto lerp(A a,B b,C c){return a+(b-a)*c;}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator*(const glm::vec<L,T,Q>& a,S b){return a*T(b);}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator*(S b,const glm::vec<L,T,Q>& a){return T(b)*a;}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator+(const glm::vec<L,T,Q>& a,S b){return a+T(b);}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator+(S b,const glm::vec<L,T,Q>& a){return T(b)+a;}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator-(const glm::vec<L,T,Q>& a,S b){return a-T(b);}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator-(S b,const glm::vec<L,T,Q>& a){return T(b)-a;}
template<glm::length_t L,typename T,glm::qualifier Q,typename S>requires(std::is_arithmetic_v<S>&&!std::is_same_v<T,S>)auto operator/(const glm::vec<L,T,Q>& a,S b){return a/T(b);}
template<int L>glm::vec<L,float> half(glm::vec<L,float> value){for(int i=0;i<L;i++)value[i]=glm::unpackHalf1x16(glm::packHalf1x16(value[i]));return value;}
template<typename T>struct Texture2D {
    int width,height;std::shared_ptr<std::vector<T>> storage;std::vector<T>& data;
    Texture2D(int w,int h):width(w),height(h),storage(std::make_shared<std::vector<T>>(w*h)),data(*storage){}
    T load(int x,int y)const{return data[std::clamp(y,0,height-1)*width+std::clamp(x,0,width-1)];}
    T SampleLevel(int sampler,float2 uv,int,int2 offset=int2(0))const {
        float2 pixel=uv*float2(width,height)+float2(offset);
        if(!sampler)return load(int(std::floor(pixel.x)),int(std::floor(pixel.y)));
        pixel-=.5f;int x=int(std::floor(pixel.x)),y=int(std::floor(pixel.y));float2 fraction=pixel-float2(x,y);float scale=float(1u<<samplerPrecisionBits);fraction=glm::roundEven(fraction*scale)/scale;int2 carry(glm::floor(fraction));x+=carry.x;y+=carry.y;fraction-=float2(carry);
        T a=load(x,y)+(load(x+1,y)-load(x,y))*fraction.x,b=load(x,y+1)+(load(x+1,y+1)-load(x,y+1))*fraction.x;
        return a+(b-a)*fraction.y;
    }
};
inline std::vector<uint8_t> read(const char* path){std::ifstream input(path,std::ios::binary);if(!input)throw std::runtime_error("Missing CPU fixture");return {std::istreambuf_iterator<char>(input),{}};}
